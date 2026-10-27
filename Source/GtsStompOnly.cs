using HarmonyLib;
using UnityEngine;

namespace GTS
{
	// A female zombie chasing a tiny player (scale <= StompOnlyMaxScale, zombie stomp on) only wants to stomp: her
	// approach AI never swings or bites, and instead of stopping at arm's length she keeps walking until the
	// victim is under her feet, where GtsDriver starts an encounter. Breaking blocks to get there still works
	// (that goes through other AI tasks). Server side, like all zombie AI.
	[HarmonyPatch(typeof(EAIApproachAndAttackTarget), nameof(EAIApproachAndAttackTarget.Update))]
	public static class StompOnlyApproachPatch
	{
		public static bool Active;   // inside an Update whose target she only wants to stomp

		public static void Prefix(EAIApproachAndAttackTarget __instance)
		{
			Active = IsStompOnly(__instance.theEntity, __instance.entityTarget);
		}

		public static void Postfix(EAIApproachAndAttackTarget __instance)
		{
			if (!Active)
			{
				return;
			}
			Active = false;
			EntityAlive z = __instance.theEntity;
			EntityAlive v = __instance.entityTarget;
			Vector3 d = v.position - z.position;
			float dy = d.y;
			d.y = 0f;
			// the AI stops at arm's length; walk the rest of the way in
			if (d.magnitude > GtsConfig.StompTriggerRange * 0.7f && d.magnitude < 3f && Mathf.Abs(dy) < 1f
				&& !GtsPins.IsBusy(z.entityId) && z.moveHelper != null)
			{
				z.moveHelper.SetMoveTo(v.position, false);
			}
		}

		public static bool IsStompOnly(EntityAlive _zombie, EntityAlive _target)
		{
			return _zombie is EntityZombie z && _target is EntityPlayer p && !p.IsDead()
				&& GtsConfig.IsShrunk(p) && GtsConfig.GetScale(p) <= GtsConfig.StompOnlyMaxScale
				&& GtsConfig.ZombiesCanStomp(p) && GtsZombies.IsFemale(z);
		}
	}

	[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.Attack))]
	public static class StompOnlyAttackPatch
	{
		public static bool Prefix(ref bool __result)
		{
			if (StompOnlyApproachPatch.Active)
			{
				__result = false;
				return false;
			}
			return true;
		}
	}
}
