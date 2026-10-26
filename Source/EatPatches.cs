using HarmonyLib;

namespace GTS
{
	// Zombies leave a squashed player's remains alone. Corpse eating is two AI tasks: one picks the nearest corpse
	// as attack target, the other walks up to a dead attack target and eats it. Both run on the server.
	public static class EatPatches
	{
		public static bool IsSquashedCorpse(EntityAlive _e)
		{
			return _e is EntityPlayer && _e.IsDead() && GtsDriver.Squashed.ContainsKey(_e.entityId);
		}
	}

	[HarmonyPatch(typeof(EAISetNearestCorpseAsTarget), nameof(EAISetNearestCorpseAsTarget.CanExecute))]
	public static class CorpseTargetPatch
	{
		public static void Postfix(EAISetNearestCorpseAsTarget __instance, ref bool __result)
		{
			if (__result && EatPatches.IsSquashedCorpse(__instance.targetEntity))
			{
				__instance.targetEntity = null;
				__result = false;
			}
		}
	}

	// A zombie that was already chasing the player keeps them as attack target after death and switches to eating.
	[HarmonyPatch(typeof(EAIApproachAndAttackTarget), nameof(EAIApproachAndAttackTarget.CanExecute))]
	public static class EatTargetPatch
	{
		public static void Postfix(EAIApproachAndAttackTarget __instance, ref bool __result)
		{
			if (__result && EatPatches.IsSquashedCorpse(__instance.entityTarget))
			{
				__instance.theEntity.SetAttackTarget(null, 0);
				__result = false;
			}
		}
	}

	[HarmonyPatch(typeof(EAIApproachAndAttackTarget), nameof(EAIApproachAndAttackTarget.Continue))]
	public static class EatContinuePatch
	{
		public static void Postfix(EAIApproachAndAttackTarget __instance, ref bool __result)
		{
			if (__result && EatPatches.IsSquashedCorpse(__instance.entityTarget))
			{
				__instance.theEntity.SetAttackTarget(null, 0);
				__result = false;
			}
		}
	}
}
