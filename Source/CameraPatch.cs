using HarmonyLib;
using UnityEngine;

namespace GTS
{
	// Lower the local camera toward the feet while shrunk, so the world looks huge.
	[HarmonyPatch(typeof(vp_FPCamera), nameof(vp_FPCamera.LateUpdate))]
	public static class CameraPatch
	{
		private static Vector3 lastOut;

		public static void Postfix(vp_FPCamera __instance)
		{
			vp_FPController fpc = __instance.FPController;
			EntityPlayerLocal player = fpc != null ? fpc.localPlayer : null;
			if (player == null || player.AttachedToEntity != null || !GtsConfig.IsShrunk(player))
			{
				return;
			}
			Transform t = __instance.transform;
			// LateUpdate didn't reposition the camera this frame (e.g. controller disabled); scaling again would compound.
			if (t.position == lastOut)
			{
				return;
			}
			// Scale around the same interpolated position the camera itself is built from;
			// using the entity position here makes the camera jitter while moving fast (fly mode).
			Vector3 feet = fpc.SmoothPosition;
			t.position = feet + (t.position - feet) * GtsConfig.GetScale(player);
			lastOut = t.position;
		}
	}
}
