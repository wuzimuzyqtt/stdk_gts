using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	// Client-side flattened corpse left in the blood pool: the victim's skinned meshes are baked in the pose they
	// had at the moment of the squash, laid on their back and pressed paper thin. Unlike the real player corpse it
	// outlives the respawn, and disappears together with the blood.
	public static class GtsCorpse
	{
		public static void Spawn(EntityAlive _victim, Vector3 _feet, float _scale)
		{
			Transform model = _victim != null ? _victim.ModelTransform : null;
			if (model == null)
			{
				return;
			}
			List<SkinnedMeshRenderer> parts = PickRenderers(model);
			if (parts.Count == 0)
			{
				return;
			}

			GameObject root = new GameObject("GtsFlatCorpse");
			GtsFlatCorpse owner = root.AddComponent<GtsFlatCorpse>();
			// Bake into the model's own (unscaled) space: BakeMesh leaves vertices in the renderer's local space
			// without its scale, so each part gets the renderer's full transform relative to the model. The model's
			// shrink scale cancels out here and is applied once on the root below.
			Matrix4x4 toModel = model.worldToLocalMatrix;
			foreach (SkinnedMeshRenderer smr in parts)
			{
				Mesh mesh = new Mesh();
				smr.BakeMesh(mesh);
				owner.Meshes.Add(mesh);

				GameObject part = new GameObject(smr.name);
				part.transform.SetParent(root.transform, false);
				Matrix4x4 local = toModel * smr.transform.localToWorldMatrix;
				part.transform.localPosition = local.GetColumn(3);
				part.transform.localRotation = local.rotation;
				part.transform.localScale = local.lossyScale;
				part.AddComponent<MeshFilter>().sharedMesh = mesh;
				MeshRenderer mr = part.AddComponent<MeshRenderer>();
				mr.sharedMaterials = smr.sharedMaterials;
				mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
			}

			// Lie on the back: the model's face (+Z) points up, its head (+Y) along the way the victim was facing,
			// feet at the squash spot. Then press the model's front-to-back axis (now vertical) flat.
			Vector3 head = _victim.GetForwardVector();
			head.y = 0f;
			head = head.sqrMagnitude > 0.001f ? head.normalized : Vector3.forward;
			float s = Mathf.Max(_scale, 0.001f);
			root.transform.rotation = Quaternion.LookRotation(Vector3.up, head);
			root.transform.localScale = new Vector3(s, s, s * GtsConfig.FlatCorpseThickness);
			Vector3 center = _feet - head * (GtsConfig.PlayerHeight * s * 0.5f) + Vector3.up * GtsConfig.FlatCorpseLift;
			root.transform.position = center - Origin.position;

			Object.Destroy(root, GtsConfig.BloodLifetime);
		}

		// Only what is actually drawn: LOD and hidden variants would otherwise be stacked on top of each other.
		// In first person the local body may not be rendered at all, so fall back to every renderer then.
		private static List<SkinnedMeshRenderer> PickRenderers(Transform _model)
		{
			List<SkinnedMeshRenderer> all = new List<SkinnedMeshRenderer>();
			List<SkinnedMeshRenderer> visible = new List<SkinnedMeshRenderer>();
			foreach (SkinnedMeshRenderer smr in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (smr.sharedMesh == null)
				{
					continue;
				}
				all.Add(smr);
				if (smr.enabled && smr.gameObject.activeInHierarchy)
				{
					visible.Add(smr);
				}
			}
			return visible.Count > 0 ? visible : all;
		}
	}

	// Frees the baked meshes when the corpse goes away (they are not assets and would otherwise leak).
	public class GtsFlatCorpse : MonoBehaviour
	{
		public readonly List<Mesh> Meshes = new List<Mesh>();

		private void OnDestroy()
		{
			foreach (Mesh m in Meshes)
			{
				if (m != null)
				{
					Destroy(m);
				}
			}
		}
	}
}
