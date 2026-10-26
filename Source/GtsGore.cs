using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	// Client-side squash gore: layered blood particles plus physical meat/limb/skull chunks flung out of the spot.
	// Everything here is local visuals only; the server just tells every client where the squash happened and how
	// big the victim was. Sizes and spread scale with the victim; launch speeds with sqrt(scale) so the throw
	// distance (~v^2) scales linearly too.
	public static class GtsGore
	{
		private const float BurstHeight = 0.2f;

		// Vanilla particle effect (ParticleEffects/p_<name>.prefab): the spurt a zombie makes biting into a corpse
		private const string EatBlood = "blood_eat";

		// The ground blood models behind the vanilla bloodDecor blocks, spawned directly so they can go away
		// with the flat corpse instead of being blocks in the world.
		public static readonly string[] SplatVariants = { "01", "02", "05", "06" };
		private const string SplatModel = "@:Entities/Debris/groundDecalBlood{0}Prefab.prefab";

		// Vanilla models used as chunks
		private const string MeatModel = "@:Other/Items/Misc/raw_meatPrefab.prefab";
		private const string SkullModel = "@:Entities/Gore/humanSkullGorePrefab.prefab";
		private static readonly string[] LimbModels =
		{
			"@:Entities/Zombies/Common/Dismemberment/UpperArmGore.prefab",
			"@:Entities/Zombies/Common/Dismemberment/LowerArmGore.prefab",
			"@:Entities/Zombies/Common/Dismemberment/UpperLegGore.prefab",
			"@:Entities/Zombies/Common/Dismemberment/LowerLegGore.prefab",
			"@:Entities/Zombies/Common/Dismemberment/HeadGore.prefab"
		};

		private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
		private static readonly HashSet<string> warned = new HashSet<string>();
		private static Transform inactiveHolder;

		public static void Play(Vector3 _pos, float _scale)
		{
			float s = Mathf.Clamp(_scale, GtsConfig.GoreMinScale, 1f);
			float ps = GtsConfig.GoreParticleScale * s;
			Vector3 center = _pos + Vector3.up * (BurstHeight * s);

			// 1. blood spurting up out of the spot
			for (int i = 0; i < 4; i++)
			{
				Vector3 up = (Vector3.up + Random.insideUnitSphere * 0.4f).normalized;
				Burst(EatBlood, center + Jitter(0.1f * s), Quaternion.LookRotation(up), ps * GtsConfig.GoreEatBloodScale);
			}

			// 2. and thrown outward all around
			for (int i = 0; i < 8; i++)
			{
				float a = i * Mathf.PI * 2f / 8f + Random.Range(-0.3f, 0.3f);
				Vector3 outward = new Vector3(Mathf.Cos(a), Random.Range(0.2f, 0.6f), Mathf.Sin(a)).normalized;
				Burst(EatBlood, center + outward * (0.15f * s), Quaternion.LookRotation(outward), ps * GtsConfig.GoreEatBloodScale);
			}

			// 3. chunks
			for (int i = 0; i < GtsConfig.GoreMeatChunks; i++)
			{
				Fling(MeatModel, center, s);
			}
			for (int i = 0; i < GtsConfig.GoreLimbChunks; i++)
			{
				Fling(LimbModels[Random.Range(0, LimbModels.Length)], center, s);
			}
			if (GtsConfig.GoreSkull)
			{
				Fling(SkullModel, center, s);
			}

			for (int i = 0; i < GtsConfig.SquashSounds.Length; i++)
			{
				Audio.Manager.Play(_pos, GtsConfig.SquashSounds[i]);
			}
		}

		// The blood pool under the victim, flat on whatever ground is there. Lasts as long as the flat corpse.
		public static void Splat(Vector3 _pos)
		{
			string variant = GtsConfig.BloodSplatVariant;
			if (string.IsNullOrEmpty(variant))
			{
				variant = SplatVariants[Random.Range(0, SplatVariants.Length)];
			}
			Splat(_pos, variant, GtsConfig.BloodLifetime);
		}

		public static void Splat(Vector3 _pos, string _variant, float _lifetime)
		{
			GameObject prefab = LoadPrefab(string.Format(SplatModel, _variant));
			if (prefab == null)
			{
				return;
			}
			Vector3 ground = GtsGround.Find(_pos, out Vector3 normal);
			// a block model: strip its scripts and colliders so it is just something drawn on the ground
			GameObject go = Object.Instantiate(prefab, GetInactiveHolder());
			foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
			{
				Object.DestroyImmediate(mb);
			}
			foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
			{
				Object.DestroyImmediate(c);
			}
			go.transform.SetParent(null, false);
			Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
			go.transform.SetPositionAndRotation(ground - Origin.position, rot);
			go.transform.localScale *= GtsConfig.BloodSplatScale;
			go.SetActive(true);
			GtsGround.SitOnGround(go, ground.y, GtsConfig.BloodSplatLift);
			Object.Destroy(go, _lifetime);
		}

		private static Vector3 Jitter(float _r)
		{
			Vector2 c = Random.insideUnitCircle * _r;
			return new Vector3(c.x, Random.Range(0f, _r), c.y);
		}

		private static void Burst(string _name, Vector3 _pos, Quaternion _rot, float _scale)
		{
			if (!ParticleEffect.IsAvailable(_name))
			{
				WarnOnce(_name, "particle effect not found");
				return;
			}
			Transform t = ParticleEffect.SpawnParticleEffect(new ParticleEffect(_name, _pos, _rot, 1f, Color.white), -1, true);
			if (t == null || _scale == 1f)
			{
				return;
			}
			// transform scale alone is ignored by systems in Local scaling mode, so scale the emitters directly:
			// size and speed by the same factor keeps the spray shape, just bigger or smaller
			foreach (ParticleSystem ps in t.GetComponentsInChildren<ParticleSystem>(true))
			{
				ParticleSystem.MainModule main = ps.main;
				main.startSizeMultiplier *= _scale;
				main.startSpeedMultiplier *= _scale;
				main.gravityModifierMultiplier *= Mathf.Min(1f, _scale);
			}
		}

		private static void Fling(string _path, Vector3 _center, float _scale)
		{
			GameObject prefab = LoadPrefab(_path);
			if (prefab == null)
			{
				return;
			}
			// These prefabs come from items, blocks and dismemberment and carry scripts that expect to be set up by
			// their owner. Instantiate under an inactive parent so none of them wakes up, strip everything but
			// meshes and colliders, then move it into the world.
			GameObject go = Object.Instantiate(prefab, GetInactiveHolder());
			foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
			{
				Object.DestroyImmediate(mb);
			}
			foreach (Rigidbody extra in go.GetComponentsInChildren<Rigidbody>(true))
			{
				if (extra.gameObject != go)
				{
					Object.DestroyImmediate(extra);
				}
			}
			bool hasCollider = false;
			foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
			{
				if (c.isTrigger)
				{
					Object.DestroyImmediate(c);
				}
				else
				{
					hasCollider = true;
				}
			}
			if (!hasCollider)
			{
				AddBoundsCollider(go);
			}
			foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
			{
				child.gameObject.layer = 0;   // Default collides with terrain and blocks
			}
			go.transform.SetParent(null, false);
			go.transform.SetPositionAndRotation(_center - Origin.position + Jitter(0.15f * _scale), Random.rotation);
			go.transform.localScale *= GtsConfig.GoreChunkScale * _scale;
			go.SetActive(true);

			Rigidbody rb = go.GetComponent<Rigidbody>();
			if (rb == null)
			{
				rb = go.AddComponent<Rigidbody>();
			}
			rb.mass = 1f;
			rb.interpolation = RigidbodyInterpolation.Interpolate;
			rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			float speed = Mathf.Sqrt(_scale);
			Vector2 h = Random.insideUnitCircle.normalized * (Random.Range(GtsConfig.GoreFlingMin, GtsConfig.GoreFlingMax) * speed);
			rb.velocity = new Vector3(h.x, Random.Range(GtsConfig.GoreFlingUpMin, GtsConfig.GoreFlingUpMax) * speed, h.y);
			rb.angularVelocity = Random.insideUnitSphere * 15f;

			Object.Destroy(go, GtsConfig.GoreChunkLifetime);
		}

		private static Transform GetInactiveHolder()
		{
			if (inactiveHolder == null)
			{
				GameObject holder = new GameObject("GtsGoreHolder");
				holder.SetActive(false);
				Object.DontDestroyOnLoad(holder);
				inactiveHolder = holder.transform;
			}
			return inactiveHolder;
		}

		private static void AddBoundsCollider(GameObject _go)
		{
			Renderer[] renderers = _go.GetComponentsInChildren<Renderer>(true);
			if (renderers.Length == 0)
			{
				_go.AddComponent<SphereCollider>().radius = 0.1f;
				return;
			}
			Bounds b = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++)
			{
				b.Encapsulate(renderers[i].bounds);
			}
			BoxCollider box = _go.AddComponent<BoxCollider>();
			box.center = _go.transform.InverseTransformPoint(b.center);
			Vector3 s = _go.transform.lossyScale;
			box.size = new Vector3(b.size.x / Mathf.Max(s.x, 0.0001f), b.size.y / Mathf.Max(s.y, 0.0001f), b.size.z / Mathf.Max(s.z, 0.0001f));
		}

		private static GameObject LoadPrefab(string _path, bool _warn = true)
		{
			if (prefabs.TryGetValue(_path, out GameObject cached))
			{
				return cached;
			}
			GameObject prefab = null;
			try
			{
				prefab = DataLoader.LoadAsset<GameObject>(_path);
			}
			catch (System.Exception e)
			{
				Log.Warning("[GTS] loading " + _path + " failed: " + e.Message);
			}
			if (prefab == null && _warn)
			{
				WarnOnce(_path, "model not found");
			}
			prefabs[_path] = prefab;   // cache failures too, so we only try once
			return prefab;
		}

		private static void WarnOnce(string _what, string _why)
		{
			if (warned.Add(_what))
			{
				Log.Warning("[GTS] gore: " + _why + ": " + _what);
			}
		}
	}
}
