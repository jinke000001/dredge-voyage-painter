using System;
using System.Reflection;
using UnityEngine;

namespace Jinke.DredgeMerchantTravel
{
    internal sealed class NativeManifest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo HoldField = typeof(AbilityBarUI).GetField("currentAbilityActionHold", Private);
        private static readonly FieldInfo DestinationField = typeof(TeleportAbility).GetField("teleportDestinationObject", Private);
        private static readonly MethodInfo Complete = typeof(AbilityBarUI).GetMethod("OnCurrentPressComplete", Private);
        private static readonly MethodInfo Verify = typeof(AbilityBarUI).GetMethod("VerifyAbilityCast", Private);
        private readonly SingleCommitTicket ticket = new SingleCommitTicket();
        internal bool DestinationRestored { get; private set; } = true;
        internal int OriginalDestinationId { get; private set; }
        internal int TemporaryDestinationId { get; private set; }
        internal bool Committing { get { return ticket.InProgress; } }
        internal bool Consume(AbilityBarUI owner) { return ticket.Consume(owner, owner.CurrentAbilityData); }
        internal static bool IsManifest(AbilityData data) { return data != null && string.Equals(data.name, "manifest", StringComparison.Ordinal); }
        internal static AbilityData Data { get { return GameManager.Instance.PlayerAbilities.GetAbilityDataByName("manifest"); } }
        internal static bool IsCurrent(AbilityBarUI owner)
        {
            GameManager game = GameManager.Instance;
            return game != null && game.UI != null && owner != null && game.UI.AbilityBarUI == owner &&
                IsManifest(owner.CurrentAbilityData) && game.PlayerAbilities != null &&
                game.PlayerAbilities.CurrentlySelectedAbilityData == owner.CurrentAbilityData;
        }
        internal static DredgePlayerActionHold Hold(AbilityBarUI owner)
        { return HoldField == null ? null : HoldField.GetValue(owner) as DredgePlayerActionHold; }
        internal static bool VerifyCast(AbilityBarUI owner)
        {
            if (Verify == null || !IsCurrent(owner)) return false;
            return (bool)Invoke(Verify, owner);
        }
        internal static double Remaining
        {
            get
            {
                AbilityData data = Data;
                if (data == null) throw new InvalidOperationException("Native manifest data unavailable.");
                float elapsed = GameManager.Instance.PlayerAbilities.GetTimeSinceLastCast(data);
                return RadialRules.NativeCooldownRemaining(elapsed, data.cooldown);
            }
        }
        internal static TeleportAbility Ability(AbilityData data)
        {
            var ability = GameManager.Instance.PlayerAbilities.GetAbilityForData(data) as TeleportAbility;
            if (ability == null || DestinationField == null || Complete == null) throw new InvalidOperationException("Native manifest contract unavailable.");
            return ability;
        }
        internal static Vector3 OriginalDestination(AbilityData data)
        {
            var original = DestinationField.GetValue(Ability(data)) as GameObject;
            if (original == null) throw new InvalidOperationException("Native Blackstone destination unavailable.");
            return original.transform.position;
        }
        internal void Commit(AbilityBarUI owner, AbilityData expected, Vector3? merchantPosition)
        {
            if (!IsCurrent(owner) || owner.CurrentAbilityData != expected || !VerifyCast(owner))
                throw new InvalidOperationException("Native manifest rejected the final cast.");
            TeleportAbility ability = Ability(expected);
            var original = DestinationField.GetValue(ability) as GameObject;
            if (original == null) throw new InvalidOperationException("Native Blackstone destination unavailable.");
            OriginalDestinationId = original.GetInstanceID();
            TemporaryDestinationId = 0;
            DestinationRestored = true;
            GameObject temporary = null;
            bool replaced = false;
            try
            {
                if (merchantPosition.HasValue)
                {
                    temporary = new GameObject("Jinke-Manifest-ScopedDestination");
                    temporary.hideFlags = HideFlags.HideAndDontSave;
                    temporary.SetActive(false);
                    temporary.transform.position = merchantPosition.Value;
                    TemporaryDestinationId = temporary.GetInstanceID();
                    DestinationRestored = false;
                    DestinationField.SetValue(ability, temporary);
                    replaced = true;
                }
                ticket.Begin(owner, expected);
                Invoke(Complete, owner);
                if (!ticket.Consumed) throw new InvalidOperationException("Native manifest hook did not consume the commit ticket.");
            }
            finally
            {
                // Keep the temporary target through Activate's distance/achievement check as well as Teleport.
                // PlayerTeleport's coroutine captures only Vector3; it does not retain this object.
                try
                {
                    if (replaced) DestinationField.SetValue(ability, original);
                    DestinationRestored = ReferenceEquals(DestinationField.GetValue(ability), original);
                    if (!DestinationRestored) throw new InvalidOperationException("Native manifest target could not be restored.");
                }
                finally
                {
                    ticket.End();
                    if (temporary != null) UnityEngine.Object.Destroy(temporary);
                }
            }
        }
        private static object Invoke(MethodInfo method, object owner)
        {
            try { return method.Invoke(owner, null); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
    }
}
