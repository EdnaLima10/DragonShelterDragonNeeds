using System;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using Gameplay.Common;
using Gameplay.Entities.Creatures.Dragon;
using GenericServices.Foundation.GenericServices.Runtime.StatManagement;
using GlobalServices.PersistenceStructure;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DragonShelterDragonNeedsV3
{
    internal sealed class WriteReceipt
    {
        internal DragonOwner Owner;
        internal StatType Type;
        internal float Received, Target;
        internal bool Refill;
    }

    internal sealed class Runtime
    {
        internal static Runtime Instance;
        readonly ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource("Dragon Needs V3");
        readonly DragonRegistry registry = new DragonRegistry();
        readonly Harmony harmony = new Harmony(Plugin.Id);
        readonly ConfigEntry<bool> enabled, trace;
        Controller host;
        bool ready, stopping, scanning, creatingHost;
        float nextScan, nextHeartbeat;
        long energyWrites, powerWrites, repairs;

        internal static void Start(ConfigFile config)
        {
            if (Instance != null) return;
            var runtime = new Runtime(config);
            Instance = runtime;
            try
            {
                GameContract.Verify();
                harmonyCheck(runtime);
                runtime.ready = true;
                SceneManager.sceneLoaded += runtime.SceneLoaded;
                SceneManager.sceneUnloaded += runtime.SceneUnloaded;
                runtime.EnsureHost();
                runtime.Lifecycle("V3 ativa; Energy e Power separados; Satiety/Boredom fora do controle. Aguardando prova de interceptacao.");
            }
            catch (Exception e) { runtime.Fail(e); }
        }

        static void harmonyCheck(Runtime runtime)
        {
            runtime.harmony.PatchAll(typeof(Runtime).Assembly);
            var set = AccessTools.Method(typeof(StatManager), "Set", new[] { typeof(Stat) });
            var constructor = AccessTools.Method(typeof(Dragon), "Constructor", new[] { typeof(DragonData), typeof(EntitySaveData) });
            var info = Harmony.GetPatchInfo(set);
            if (info == null || !info.Prefixes.Any(p => p.owner == Plugin.Id) || !info.Postfixes.Any(p => p.owner == Plugin.Id) ||
                !Harmony.GetPatchInfo(constructor).Postfixes.Any(p => p.owner == Plugin.Id))
                throw new InvalidOperationException("Registro Harmony incompleto.");
            runtime.Lifecycle("PATCH REGISTRADO: StatManager.Set(Stat) Prefix+Postfix; Dragon.Constructor Postfix. Ainda nao comprova consumo interceptado.");
        }

        Runtime(ConfigFile config)
        {
            enabled = config.Bind("General", "Enabled", true, "Controla somente Energy e Power dos dragoes.");
            trace = config.Bind("Diagnostics", "TraceWrites", true, "Registra entrada e valor realmente armazenado de cada Set controlado. Pode gerar bastante log.");
        }

        internal void Lifecycle(string message) => log.LogInfo(DateTime.UtcNow.ToString("O") + " " + message);
        void EnsureHost()
        {
            if (!ready || stopping || creatingHost) return;
            creatingHost = true;
            try
            {
                if (host == null)
                {
                    var go = new GameObject("DragonNeedsV3.Controller");
                    go.hideFlags = HideFlags.HideInHierarchy;
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    host = go.AddComponent<Controller>();
                    Lifecycle("Controlador independente criado com DontDestroyOnLoad.");
                }
                if (!host.gameObject.activeSelf) host.gameObject.SetActive(true);
                if (!host.enabled) host.enabled = true;
            }
            finally { creatingHost = false; }
        }

        internal void HostDestroyed(Controller value)
        {
            if (!ReferenceEquals(host, value)) return;
            host = null;
            if (!stopping) Lifecycle("Controlador destruido; patches estaticos preservados. Recriacao no proximo Set, Constructor ou evento de cena.");
        }
        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try { EnsureHost(); nextScan = 0; Lifecycle("Cena carregada: " + scene.name); }
            catch (Exception e) { Fail(e); }
        }
        void SceneUnloaded(Scene scene)
        {
            try { registry.Prune(); EnsureHost(); nextScan = 0; }
            catch (Exception e) { Fail(e); }
        }

        internal void Tick()
        {
            if (!ready || stopping) return;
            try
            {
                EnsureHost();
                if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 5; Scan(); }
                if (Time.unscaledTime >= nextHeartbeat)
                {
                    nextHeartbeat = Time.unscaledTime + 30;
                    Lifecycle("HEARTBEAT: controlador=" + (host != null && host.isActiveAndEnabled) + "; dragoes=" + registry.Count +
                        "; Energy Set=" + energyWrites + "; Power Set=" + powerWrites + "; reposicoes=" + repairs + "; Enabled=" + enabled.Value);
                }
            }
            catch (Exception e) { Fail(e); }
        }

        void Scan()
        {
            if (scanning) return;
            scanning = true;
            try
            {
                registry.Prune();
                var dragons = UnityEngine.Object.FindObjectsOfType<Dragon>(true);
                foreach (var dragon in dragons) RegisterDragon(dragon, "busca");
                if (registry.Count == 0) Lifecycle("BUSCA: encontrados=" + dragons.Length + "; registrados=0; nenhuma protecao de dragao comprovada.");
            }
            finally { scanning = false; }
        }

        internal void RegisterDragon(Dragon dragon, string source)
        {
            if (!ready || stopping) return;
            try
            {
                EnsureHost();
                bool added = registry.Register(dragon, out DragonOwner owner, out string reason);
                if (owner == null) { Lifecycle("Registro pendente: " + reason); return; }
                if (added) Lifecycle("DRAGAO CONFIRMADO: " + owner.Label + "; StatComponent/StatManager e propriedade verificados; origem=" + source);
                var types = Types();
                if (types.Length == 0 && added) Lifecycle("StatTable ainda indisponivel; nova tentativa na busca periodica.");
                foreach (var type in types)
                {
                    var stat = owner.Manager.Get(type);
                    if (stat.IsEmpty || !ReferenceEquals(stat.Type, type))
                    {
                        if (added) Lifecycle("IDENTIDADE AUSENTE: " + owner.Label + "/" + type.name + "; aguardando inicializacao; sem escrita.");
                        continue;
                    }
                    if (owner.Reported.Add(type)) Lifecycle("ATRIBUTO CONFIRMADO: " + owner.Label + "/" + type.name + " #" + type.GetInstanceID() + "; atual=" + F(stat.Value) + "; max=" + F(stat.MaxValue));
                    if (enabled.Value && stat.Value != stat.MaxValue && FullValuePolicy.TryMaximum(stat.MinValue, stat.MaxValue, out float max))
                    {
                        owner.Reconciling = true;
                        try
                        {
                            owner.Manager.Set(GameContract.WithValue(stat, max));
                            float final = owner.Manager.Get(type).Value;
                            if (final != max) throw new InvalidOperationException("Reposicao nao persistiu.");
                            repairs++;
                            GameContract.Notify(owner.Component, type, final - stat.Value);
                        }
                        finally { owner.Reconciling = false; }
                    }
                }
            }
            catch (Exception e) { Fail(e); }
        }

        static StatType[] Types()
        {
            StatType energy, power;
            // The game initializes its singleton after BepInEx. Retry on the next scan.
            try { energy = StatTable.Energy; power = StatTable.Power; }
            catch (NullReferenceException) { return new StatType[0]; }
            if (energy == null || power == null) return new StatType[0];
            if (ReferenceEquals(energy, power) || energy.name != "Stat_Energy" || power.name != "Stat_Power")
                throw new InvalidOperationException("Identidades Energy/Power invalidas.");
            return new[] { energy, power };
        }

        internal void BeforeStore(StatManager manager, ref Stat value, out WriteReceipt receipt)
        {
            receipt = null;
            if (!ready || stopping || !enabled.Value || value.IsEmpty) return;
            try
            {
                EnsureHost();
                // Unknown managers are never authorized by their stat names alone.
                if (!registry.TryGet(manager, out DragonOwner owner)) return;
                var type = value.Type;
                if (!Types().Any(t => ReferenceEquals(t, type))) return;
                if (!FullValuePolicy.TryMaximum(value.MinValue, value.MaxValue, out float target)) return;
                receipt = new WriteReceipt { Owner = owner, Type = value.Type, Received = value.Value, Target = target, Refill = owner.Reconciling };
                value = GameContract.WithValue(value, target);
            }
            catch (Exception e) { Fail(e); }
        }

        internal void AfterStore(StatManager manager, WriteReceipt receipt)
        {
            if (receipt == null || !ready || stopping) return;
            try
            {
                var stored = manager.Get(receipt.Type);
                if (stored.IsEmpty || stored.Value != receipt.Target) throw new InvalidOperationException("Set nao armazenou o maximo esperado: " + receipt.Owner.Label + "/" + receipt.Type.name);
                if (ReferenceEquals(receipt.Type, StatTable.Energy)) energyWrites++; else powerWrites++;
                if (trace.Value) Lifecycle("SET CONFIRMADO: dragao=" + receipt.Owner.Label + "; atributo=" + receipt.Type.name +
                    "; recebido=" + F(receipt.Received) + "; final=" + F(stored.Value) + "; max=" + F(receipt.Target) +
                    "; origem=" + (receipt.Refill ? "reposicao" : "chamada do jogo"));
            }
            catch (Exception e) { Fail(e); }
        }

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        void Fail(Exception e) { log.LogError("V3 INTERROMPIDA: " + e); Stop(); }
        internal void Stop()
        {
            if (stopping) return;
            stopping = true;
            ready = false;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            harmony.UnpatchSelf();
            registry.Clear();
            Lifecycle("V3 encerrada; patches removidos por encerramento/falha explicita.");
        }
    }
}
