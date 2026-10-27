using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GTS
{
	// Every local movement speed (walk, run, crouch, fly) goes through this, so one multiplier scales them all
	// without interfering with perk/buff percentages.
	[HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.GetSpeedModifier))]
	public static class SpeedPatch
	{
		public static void Postfix(EntityPlayerLocal __instance, ref float __result)
		{
			if (GtsPinFx.LocalPinned)
			{
				__result = 0f;
			}
			else if (GtsConfig.IsShrunk(__instance))
			{
				__result *= GtsConfig.GetSpeedMult(__instance);
			}
		}
	}

	// The walk/run animation is picked from speedForward/speedStrafe, which the game derives from the distance
	// actually moved (and truncates to 0.01). A tiny player moves tiny distances, so it always looked like walking.
	// Feed the animator the equivalent full-size speed instead; the values are also what remote clients animate from.
	[HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.updateSpeedForwardAndStrafe))]
	public static class AnimSpeedPatch
	{
		public static void Postfix(EntityPlayerLocal __instance, Vector3 _dist)
		{
			if (__instance.isLadderAttached || !GtsConfig.IsShrunk(__instance))
			{
				return;
			}
			Vector3 d = _dist / GtsConfig.GetScale(__instance);
			__instance.speedForward = 0f;
			__instance.speedStrafe = 0f;
			if (Mathf.Abs(d.x) > 0.001f || Mathf.Abs(d.z) > 0.001f)
			{
				Vector3 v = __instance.transform.InverseTransformDirection(d).normalized * d.magnitude;
				__instance.speedForward = (int)(v.z * 100f) / 100f;
				__instance.speedStrafe = (int)(v.x * 100f) / 100f;
			}
			__instance.SetMovementState();
		}
	}

	// vp_FPController.FixedMove sets stepOffset to a hard-coded 0.1 whenever the player is airborne or blocked.
	// Unity rejects (and logs an error for) a step offset taller than the controller, which a tiny player is
	// shorter than, so replace that constant with min(0.1, height).
	[HarmonyPatch(typeof(vp_FPController), nameof(vp_FPController.FixedMove))]
	public static class StepOffsetPatch
	{
		private static readonly MethodInfo setStepOffset = AccessTools.PropertySetter(typeof(CharacterController), nameof(CharacterController.stepOffset));
		private static readonly MethodInfo clamp = AccessTools.Method(typeof(StepOffsetPatch), nameof(ClampAirStep));

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _instructions)
		{
			List<CodeInstruction> code = new List<CodeInstruction>(_instructions);
			int patched = 0;
			for (int i = 0; i < code.Count - 1; i++)
			{
				if (code[i].opcode == OpCodes.Ldc_R4 && (float)code[i].operand == 0.1f && code[i + 1].Calls(setStepOffset))
				{
					// stack before: cc ; after: cc, min(0.1, cc.height)
					code[i] = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[i]);
					code.Insert(i + 1, new CodeInstruction(OpCodes.Call, clamp));
					patched++;
				}
			}
			if (patched == 0)
			{
				Log.Warning("[GTS] StepOffsetPatch: pattern not found in vp_FPController.FixedMove, tiny players may log step offset errors");
			}
			return code;
		}

		public static float ClampAirStep(vp_FPController _fpc)
		{
			CharacterController cc = _fpc.CharacterController;
			return cc != null ? Mathf.Min(0.1f, cc.height) : 0.1f;
		}
	}
}
