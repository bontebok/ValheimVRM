﻿using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections;
using System.Diagnostics;
using HarmonyLib;
using UniGLTF;
using UnityEngine;
using VRM;
using VRMShaders;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;


namespace ValheimVRM
{
    public class VRM 
	{
		public enum SourceType
		{
			Local,  // my VRM from my computer
			Shared // VRM, downloaded from other player
		}
		
		public GameObject VisualModel { get; private set; }
		public byte[] Src;
		public byte[] SrcHash;
		public byte[] SettingsHash;
		public string Name { get; private set; }
		public SourceType Source = SourceType.Local;

		public VRM(GameObject visualModel, string name)
		{
			VisualModel = visualModel;
			Name = name;
		}

		~VRM()
		{
			if (VisualModel != null)
			{
				Object.Destroy(VisualModel);
			}
		}

		public void RecalculateSrcBytesHash()
		{
			Task.Run(() =>
			{
				using (var md5 = System.Security.Cryptography.MD5.Create())
				{
					var hash = md5.ComputeHash(Src);
					lock (this)
					{
						SrcHash = hash;
					}
				}
			});
		}



		public void RecalculateSettingsHash()
		{
			Task.Run(() =>
			{
				using (var md5 = System.Security.Cryptography.MD5.Create())
				{
					byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(Settings.GetSettings(Name).ToStringDiffOnly());
					
					lock (this)
					{
						SettingsHash =  md5.ComputeHash(inputBytes);;
					}
				}
			});
		}
		public class Timer : IDisposable
		{
			private Stopwatch stopwatch;
			private string name;

			public Timer(string name)
			{
				this.name = name;
				this.stopwatch = Stopwatch.StartNew();
			}

			public void Dispose()
			{
				stopwatch.Stop();
				Debug.Log($"{name} took {stopwatch.ElapsedMilliseconds} ms");
			}
		}

		public static GameObject ImportVisual(byte[] buf, string path, float scale)
		{
			Debug.Log("[ValheimVRM] loading vrm: " + buf.Length + " bytes");
			try
			{
				var data = new GlbBinaryParser(buf, path).Parse();
				var vrm = new VRMData(data);
				var context = new VRMImporterContext(vrm);
				var loaded = default(RuntimeGltfInstance);
				
				try
				{
					loaded = context.Load();
				}
				catch (TypeLoadException ex)
				{
					Debug.LogError("Failed to load type: " + ex.TypeName);
					Debug.LogError(ex);
				}
				loaded.ShowMeshes();
				loaded.Root.transform.localScale = Vector3.one * scale;

				Debug.Log("[ValheimVRM] VRM read successful");

				return loaded.Root;
			}
			catch (Exception ex)
			{
				Debug.LogError(ex);
			}

			return null;
		}
		
 

		public static IEnumerator ImportVisualAsync(byte[] buf, string path, float scale, Action<GameObject> onCompleted)
		{
 
			Debug.Log("[ValheimVRM Async] loading vrm: " + buf.Length + " bytes");

			var data = new GlbBinaryParser(buf, path).Parse();
			yield return null;

			var vrm = new VRMData(data);
			yield return null;

			
			var context = new VRMImporterContext(vrm, null, new TextureDeserializerAsync());
 
			//var loader = context.LoadAsync(new RuntimeOnlyAwaitCaller(0.001f), name => new Timer(name));					
			var loader = context.LoadAsync(new RuntimeOnlyAwaitCaller(0.001f));					
			while (!loader.IsCompleted)
			{
				yield return new WaitUntil(() => loader.IsCompleted);
			}
			try
			{
				
				var loaded = loader.Result;
			
				loaded.ShowMeshes();

				loaded.Root.transform.localScale = Vector3.one * scale;
				Debug.Log("[ValheimVRM] VRM read successful");
				onCompleted(loaded.Root);
			}
			catch (Exception ex)
			{
				Debug.LogError("Error during VRM loading: " + ex);
			}
			
			
 	
		}
		
		public static async Task<GameObject> ImportVisualAsync(byte[] buf, string path, float scale)
		{
			Debug.Log("[ValheimVRM Async] loading vrm: " + buf.Length + " bytes");
			
			GltfData data = new GlbBinaryParser(buf, path).Parse();

			var vrm = new VRMData(data);
			
			RuntimeGltfInstance loaded;
			
			using(VRMImporterContext loader = new VRMImporterContext(vrm))
			{
				loaded = await loader.LoadAsync(new RuntimeOnlyAwaitCaller(0.001f));
			}

			try
			{
				loaded.ShowMeshes();
				loaded.Root.transform.localScale = Vector3.one * scale;
				Debug.Log("[ValheimVRM] VRM read successful");

				return loaded.Root;
			}
			catch (Exception ex)
			{
				Debug.LogError("Error during VRM loading: " + ex);
			}

			return null;
		}
		
 
		public IEnumerator SetToPlayer(Player player)
		{
			var animator = player.GetComponentInChildren<Animator>();

			var vrmController = player.GetComponent<VrmController>();
			
			
			
			var settings = Settings.GetSettings(Name);
			player.m_maxInteractDistance *= settings.InteractionDistanceScale;
 
			var vrmModel = Object.Instantiate(VisualModel);
			VrmManager.PlayerToVrmInstance[player] = vrmModel;
			vrmModel.name = "VRM_Visual";
			vrmModel.SetActive(true);
			vrmController.visual = vrmModel;

			var oldModel = animator.transform.parent.Find("VRM_Visual");
			if (oldModel != null)
			{
				Object.Destroy(oldModel);
			}
			
			vrmModel.transform.SetParent(animator.transform.parent, false);;

			float newHeight = settings.PlayerHeight;
			float newRadius = settings.PlayerRadius;

			var rigidBody = player.GetComponent<Rigidbody>();
			var collider = player.GetComponent<CapsuleCollider>();
			
			collider.height = newHeight;
			collider.radius = newRadius;
			collider.center = new Vector3(0, newHeight / 2, 0);
			
			
			rigidBody.centerOfMass = collider.center;
			
			yield return null;

			foreach (var smr in player.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>())
			{
				smr.forceRenderingOff = true;
				smr.updateWhenOffscreen = true;
				yield return null;
			}

			var orgAnim = AccessTools.FieldRefAccess<Player, Animator>(player, "m_animator");
			orgAnim.keepAnimatorStateOnDisable = true;
			orgAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			yield return null;

			vrmModel.transform.localPosition = orgAnim.transform.localPosition;

			// アニメーション同期

			var animationSync = vrmModel.GetComponent<VRMAnimationSync>();
			
			if (animationSync == null)
			{
				animationSync = vrmModel.AddComponent<VRMAnimationSync>();
				animationSync.Setup(orgAnim, settings, false);
			}
			else
			{
				animationSync.Setup(orgAnim, settings, false);
			}
			yield return null;

			// カメラ位置調整
			if (settings.FixCameraHeight)
			{
				var vrmEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
					
				if (vrmEye == null)
				{
					vrmEye = animator.GetBoneTransform(HumanBodyBones.Head);
				}

				if (vrmEye == null)
				{
					vrmEye = animator.GetBoneTransform(HumanBodyBones.Neck);
				}
				
				if (vrmEye != null)
				{
					var vrmEyePostSync = player.gameObject.GetComponent<VRMEyePositionSync>();
					if ( vrmEyePostSync == null)
					{
						vrmEyePostSync = player.gameObject.AddComponent<VRMEyePositionSync>();
						vrmEyePostSync.Setup(vrmEye);
					}
					else
					{
						vrmEyePostSync.Setup(vrmEye);
					}
				}
			}
			yield return null;

			// MToonの場合環境光の影響をカラーに反映する
			if (settings.UseMToonShader)
			{
				var mToonColorSync = vrmModel.GetComponent<MToonColorSync>();

				if (mToonColorSync == null)
				{
					mToonColorSync = vrmModel.AddComponent<MToonColorSync>();
					mToonColorSync.Setup(vrmModel);
				}
				else
				{
					mToonColorSync.Setup(vrmModel);
				}
			}
			yield return null;

			// SpringBone設定
			foreach (var springBone in vrmModel.GetComponentsInChildren<VRMSpringBone>())
			{
				springBone.m_stiffnessForce *= settings.SpringBoneStiffness;
				springBone.m_gravityPower *= settings.SpringBoneGravityPower;
				springBone.m_updateType = VRMSpringBone.SpringBoneUpdateType.FixedUpdate;
				springBone.m_center = null;
				yield return null;
			}
			
			player.GetComponent<VrmController>().ReloadSpringBones();
        }
    }
}