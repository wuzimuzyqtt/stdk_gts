using UnityEngine;

namespace GTS
{
	// Server -> clients: "this player was just squashed here", play the gore locally and hide the body.
	public class NetPackageGtsFx : NetPackage
	{
		private int entityId;
		private Vector3 pos;

		public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

		public NetPackageGtsFx Setup(int _entityId, Vector3 _pos)
		{
			entityId = _entityId;
			pos = _pos;
			return this;
		}

		public override void read(PooledBinaryReader _br)
		{
			entityId = _br.ReadInt32();
			pos = new Vector3(_br.ReadSingle(), _br.ReadSingle(), _br.ReadSingle());
		}

		public override void write(PooledBinaryWriter _bw)
		{
			base.write(_bw);
			_bw.Write(entityId);
			_bw.Write(pos.x);
			_bw.Write(pos.y);
			_bw.Write(pos.z);
		}

		public override void ProcessPackage(World _world, GameManager _callbacks)
		{
			if (_world != null)
			{
				PlayLocal(entityId, pos);
			}
		}

		public override int GetLength()
		{
			return 16;
		}

		public static void PlayLocal(int _entityId, Vector3 _pos)
		{
			GtsDriver.Squashed.Add(_entityId);
			if (GameManager.IsDedicatedServer)
			{
				return;
			}
			for (int i = 0; i < GtsConfig.BloodBursts; i++)
			{
				Vector2 r = Random.insideUnitCircle * 0.6f;
				Vector3 p = _pos + new Vector3(r.x, Random.Range(0.05f, 0.5f), r.y);
				Quaternion rot = Quaternion.Euler(Random.Range(-90f, 0f), Random.Range(0f, 360f), 0f);
				ParticleEffect.SpawnParticleEffect(new ParticleEffect(GtsConfig.BloodParticle, p, rot, 1f, Color.white), -1, true);
			}
			for (int i = 0; i < GtsConfig.SquashSounds.Length; i++)
			{
				Audio.Manager.Play(_pos, GtsConfig.SquashSounds[i]);
			}
		}
	}
}
