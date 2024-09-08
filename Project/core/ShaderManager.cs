using Godot;
using Godot.Collections;
using Project.Gameplay.Triggers;

namespace Project.Core
{
	/// <summary> Handles shader precompilation before every stage can begin. </summary>
	public partial class ShaderManager : Control
	{
		public static ShaderManager Instance;
		public bool IsCompilingShaders { get; private set; }

		[Export]
		private SubViewport shaderCompilationViewport;
		[Export]
		private Node3D shaderParent;
		[Export]
		private MeshInstance3D[] meshInstances;
		[Export]
		private GpuParticles3D[] particles;

		private int TotalShaderCount { get; set; }

		private int cullingTriggerIndex;
		private bool isSecondaryCullingCompilation;
		private readonly Array<CullingTrigger> cullingTriggers = [];

		private int meshCompilationIndex;
		private int materialCompilationIndex;
		private int particleCompilationIndex;
		private readonly Array<Mesh> meshes = [];
		private readonly Array<Material> materials = [];
		private readonly Array<Mesh> particleMeshes = [];
		private readonly Array<Material> particleMaterials = [];

		public void RegisterCullingTrigger(CullingTrigger trigger) => cullingTriggers.Add(trigger);

		public override void _EnterTree() => Instance = this;

		public override void _Process(double _)
		{
			if (!IsCompilingShaders) return;

			if (materialCompilationIndex < materials.Count ||
				meshCompilationIndex < meshes.Count ||
				particleCompilationIndex < particleMaterials.Count)
			{
				ProcessMaterials();
				return;
			}

			ProcessTriggers();
		}

		private void ProcessMaterials()
		{
			if (materialCompilationIndex < materials.Count)
			{
				// Compile normal materials 
				Material m = materials[materialCompilationIndex];
				for (int i = 0; i < meshInstances.Length; i++)
					meshInstances[i].MaterialOverride = m;

				materialCompilationIndex++;
			}

			if (meshCompilationIndex < meshes.Count)
			{
				for (int i = 0; i < meshInstances.Length; i++)
					meshInstances[i].Mesh = meshes[meshCompilationIndex];

				meshCompilationIndex++;
			}

			if (particleCompilationIndex < particleMaterials.Count)
			{
				for (int i = 0; i < particles.Length; i++)
				{
					particles[i].ProcessMaterial = particleMaterials[i];
					particles[i].DrawPass1 = particleMeshes[i];
					particles[i].Restart();
				}

				particleCompilationIndex++;
			}

			TransitionManager.instance.UpdateLoadingText("load_cache", materialCompilationIndex + particleCompilationIndex + meshCompilationIndex, TotalShaderCount);
		}

		private void ProcessTriggers()
		{
			if (cullingTriggerIndex < cullingTriggers.Count)
			{
				// Move to next level chunk
				cullingTriggers[cullingTriggerIndex].Visible = isSecondaryCullingCompilation;
				cullingTriggerIndex++;
				TransitionManager.instance.UpdateLoadingText("load_lighting", cullingTriggerIndex, cullingTriggers.Count);
				return;
			}

			if (!isSecondaryCullingCompilation)
			{
				cullingTriggerIndex = 0;
				isSecondaryCullingCompilation = true;
				return;
			}

			FinishCompilation();
		}

		public void StartCompilation()
		{
			// No shaders to compile!
			if (materials.Count == 0 && particleMaterials.Count == 0) return;

			Viewport mainViewport = GetViewport();
			shaderCompilationViewport.ScreenSpaceAA = mainViewport.ScreenSpaceAA;
			shaderCompilationViewport.Msaa3D = mainViewport.Msaa3D;
			shaderCompilationViewport.Scaling3DMode = mainViewport.Scaling3DMode;
			shaderCompilationViewport.Scaling3DScale = mainViewport.Scaling3DScale;
			shaderCompilationViewport.World3D.Environment = mainViewport.World3D.Environment;

			Engine.MaxFps = 0; // Uncap framerate for faster shader caching
			if (DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled)
				DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); // Disable vsync

			Visible = shaderParent.Visible = true;
			isSecondaryCullingCompilation = false;
			TotalShaderCount = materials.Count + meshes.Count + particleMaterials.Count;
			meshCompilationIndex = materialCompilationIndex = particleCompilationIndex = 0;
			TransitionManager.instance.UpdateLoadingText("load_cache", 0, TotalShaderCount);

			IsCompilingShaders = true;
		}

		public void FinishCompilation()
		{
			for (int i = 0; i < particles.Length; i++)
			{
				particles[i].Emitting = false;
				particles[i].ProcessMaterial = null;
				particles[i].MaterialOverride = null;
			}

			for (int i = 0; i < meshInstances.Length; i++)
			{
				meshInstances[i].MaterialOverride = null;
				meshInstances[i].Mesh = new QuadMesh();
			}

			materials.Clear();
			meshes.Clear();
			cullingTriggers.Clear();
			IsCompilingShaders = false;
			Visible = shaderParent.Visible = false;

			Engine.MaxFps = SaveManager.FrameRates[SaveManager.Config.framerate]; // Recap framerate
			if (SaveManager.Config.useVsync) // Reapply v-sync
				DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
		}

		public void QueueMaterial(Material material)
		{
			if (materials.Contains(material)) return;
			materials.Add(material);
		}

		public void QueueMesh(Mesh mesh)
		{
			if (meshes.Contains(mesh)) return;

			meshes.Add(mesh);
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
				QueueMaterial(mesh.SurfaceGetMaterial(i));
		}

		public void QueueParticle(Material material, Mesh mesh)
		{
			if (particleMeshes.Contains(mesh)) return;
			particleMeshes.Add(mesh);
			particleMaterials.Add(material);
		}
	}
}
