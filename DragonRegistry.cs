using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Gameplay.Entities.Creatures.Dragon;
using Gameplay.Entities.GenericComponents.StatStructure;
using GenericServices.Foundation.GenericServices.Runtime.StatManagement;

namespace DragonShelterDragonNeedsV3
{
    internal sealed class DragonOwner
    {
        internal readonly WeakReference<Dragon> Dragon;
        internal readonly StatComponent Component;
        internal readonly StatManager Manager;
        internal readonly string Label;
        internal readonly HashSet<StatType> Reported = new HashSet<StatType>();
        internal bool Reconciling;

        internal DragonOwner(Dragon dragon, StatComponent component, StatManager manager)
        {
            Dragon = new WeakReference<Dragon>(dragon);
            Component = component;
            Manager = manager;
            Label = dragon.name + " #" + dragon.GetInstanceID();
        }

        internal bool Valid()
        {
            return Dragon.TryGetTarget(out Dragon dragon) && dragon != null &&
                ReferenceEquals(dragon.Access<IStatHandler>(), Component) &&
                ReferenceEquals(GameContract.Manager.GetValue(Component), Manager);
        }
    }

    internal sealed class DragonRegistry
    {
        readonly Dictionary<StatManager, DragonOwner> owners = new Dictionary<StatManager, DragonOwner>(IdentityComparer<StatManager>.Instance);
        internal int Count => owners.Count;
        internal DragonOwner[] Snapshot()
        {
            var result = new DragonOwner[owners.Count];
            owners.Values.CopyTo(result, 0);
            return result;
        }
        internal bool TryGet(StatManager manager, out DragonOwner owner)
        {
            if (!owners.TryGetValue(manager, out owner)) return false;
            if (owner.Valid()) return true;
            owners.Remove(manager);
            owner = null;
            return false;
        }
        internal bool Register(Dragon dragon, out DragonOwner owner, out string reason)
        {
            owner = null;
            reason = "objeto destruido ou nulo";
            if (dragon == null) return false;
            var component = dragon.Access<IStatHandler>() as StatComponent;
            if (component == null) { reason = "StatComponent ainda indisponivel"; return false; }
            var manager = GameContract.Manager.GetValue(component) as StatManager;
            if (manager == null) { reason = "StatManager indisponivel"; return false; }
            if (TryGet(manager, out owner))
            {
                if (!owner.Dragon.TryGetTarget(out Dragon prior) || !ReferenceEquals(prior, dragon))
                    throw new InvalidOperationException("StatManager compartilhado entre dragoes; controle interrompido.");
                reason = "ja registrado";
                return false;
            }
            owner = new DragonOwner(dragon, component, manager);
            if (!owner.Valid()) { owner = null; reason = "vinculo de propriedade nao confirmado"; return false; }
            owners.Add(manager, owner);
            reason = "registrado";
            return true;
        }
        internal int Prune()
        {
            int count = 0;
            foreach (var owner in Snapshot())
                if (!owner.Valid()) { owners.Remove(owner.Manager); count++; }
            return count;
        }
        internal void Clear() => owners.Clear();
    }

    internal sealed class IdentityComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly IdentityComparer<T> Instance = new IdentityComparer<T>();
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

