using BepInEx;
using UnityEngine;

namespace DragonShelterDragonNeedsV3
{
    [BepInPlugin(Id, "Dragon Shelter Dragon Needs V3", "3.0.0")]
    [BepInIncompatibility("dragonshelter.dragonneeds")]
    [BepInIncompatibility("dragonshelter.dragonneeds.v2")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "dragonshelter.dragonneeds.v3";
        void Awake() => Runtime.Start(Config);
        // BepInEx's component is only a bootstrapper. Its lifetime does not own the patches.
        void OnDisable() => Runtime.Instance?.Lifecycle("Bootstrap desabilitado; controlador independente permanece ativo.");
        void OnDestroy() => Runtime.Instance?.Lifecycle("Bootstrap destruido; patches e controlador independente preservados.");
        void OnApplicationQuit() => Runtime.Instance?.Stop();
    }

    public sealed class Controller : MonoBehaviour
    {
        void Update() => Runtime.Instance?.Tick();
        void OnEnable() => Runtime.Instance?.Lifecycle("Controlador habilitado.");
        void OnDisable() => Runtime.Instance?.Lifecycle("Controlador desabilitado; interceptacao estatica continua ativa; recuperacao no proximo evento/chamada.");
        void OnDestroy() => Runtime.Instance?.HostDestroyed(this);
        void OnApplicationQuit() => Runtime.Instance?.Stop();
    }
}
