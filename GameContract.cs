using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Gameplay.Entities.Creatures.Dragon;
using Gameplay.Entities.GenericComponents.StatStructure;
using GenericServices.Foundation.GenericServices.Runtime.StatManagement;
using HarmonyLib;

namespace DragonShelterDragonNeedsV3
{
    internal static class GameContract
    {
        internal static readonly FieldInfo Manager = AccessTools.Field(typeof(StatComponent), "_statManager");
        internal static readonly FieldInfo Raw = AccessTools.Field(typeof(Stat), "_rawValue");
        internal static readonly FieldInfo Changed = AccessTools.Field(typeof(StatComponent), "StatChanged");

        internal static void Verify()
        {
            Check(typeof(Dragon).Assembly.Location, "3f05c2d74b0fb515f355738bc6681ada56b8594b45fc18556dcf89842ad42a0e");
            Check(typeof(StatManager).Assembly.Location, "e0b0e9c75b1684db7bb6000f5a8e9994d6415dc9eaf75c81411c47bb462f73b9");
            Check(typeof(Entities.IEntity).Assembly.Location, "8d0759d8410c40d04ade28bd515b9f53a553b0d5226cfc12c9679fb402e46dea");
            if (Manager?.FieldType != typeof(StatManager) || Raw?.FieldType != typeof(float) ||
                Changed?.FieldType != typeof(Action<StatType, float>))
                throw new MissingFieldException("Contrato de armazenamento/eventos diferente da instalacao investigada.");
        }

        static void Check(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
            {
                var actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (actual != expected) throw new InvalidOperationException("Hash incompativel: " + Path.GetFileName(path));
            }
        }

        internal static Stat WithValue(Stat original, float value)
        {
            // Preserve the exact override fields, including the game's zero sentinel.
            object boxed = original;
            Raw.SetValue(boxed, value);
            return (Stat)boxed;
        }

        internal static void Notify(StatComponent component, StatType type, float delta)
        {
            if (delta != 0 && FullValuePolicy.Finite(delta))
                (Changed.GetValue(component) as Action<StatType, float>)?.Invoke(type, delta);
        }
    }
}

