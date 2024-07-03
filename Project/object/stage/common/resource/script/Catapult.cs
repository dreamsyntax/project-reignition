using Godot;
using Project.Core;

namespace Project.Gameplay.Objects;

/// <summary>
/// Launches the player a variable amount, using <see cref="launchPower"/> as the blend of close and far settings
/// </summary>
[Tool]
public partial class Catapult : Launcher
{
	[Signal]
	public delegate void PlayerEnteredEventHandler();
	[Signal]
	public delegate void PlayerExitedEventHandler();

	private bool isProcessing;
	private CatapultState currentState;
	private enum CatapultState
	{
		Disabled,
		Enter,
		Control,
		Eject,
	}

	private float targetLaunchPower;
	private float launchPowerVelocity;
	/// <summary> How much to change launchPower per-frame. </summary>
	private readonly float PowerAdjustmentSpeed = .14f; // How fast to adjust the power
	/// <summary> The strength of the shot, between 0 and 1. Exported for easier editing in the editor. </summary>
	private readonly float PowerAdjustmentSmoothing = .2f;

	[ExportGroup("Components")]
	[Export]
	private Node3D playerPositionNode;
	[Export]
	private Node3D armNode;
	[Export]
	private AudioStreamPlayer3D enterSFX;
	[Export]
	private AudioStreamPlayer3D aimSFX;
	private Tween tweener;

	public override void _PhysicsProcess(double _)
	{
		if (Engine.IsEditorHint())
		{
			UpdateArmRotation();
			return;
		}

		if (!isProcessing)
		{
			if (aimSFX.Playing)
				SoundManager.FadeAudioPlayer(aimSFX);

			return;
		}

		if (currentState == CatapultState.Eject) // Launch the player at the right time
		{
			if (armNode.Rotation.X > Mathf.Pi * .5f)
			{
				Activate();
				return;
			}

			Character.UpdateExternalControl();
			return;
		}

		if (currentState == CatapultState.Control)
			ProcessControls();
	}

	private void ProcessControls()
	{
		// Check for state changes
		if (Input.IsActionJustPressed("button_jump"))
		{
			EjectPlayer(true);
			return;
		}

		if (Input.IsActionJustPressed("button_action"))
		{
			EjectPlayer(false);
			return;
		}

		// Update launch power
		targetLaunchPower += Character.InputVertical * PowerAdjustmentSpeed;
		targetLaunchPower = Mathf.Clamp(targetLaunchPower, 0, 1);
		launchRatio = ExtensionMethods.SmoothDamp(launchRatio, targetLaunchPower, ref launchPowerVelocity, PowerAdjustmentSmoothing);

		aimSFX.VolumeDb = Mathf.LinearToDb(Mathf.Abs(launchRatio - targetLaunchPower) / .1f);
		if (!aimSFX.Playing)
			aimSFX.Play();

		UpdateArmRotation();
		Character.UpdateExternalControl();
	}

	private void UpdateArmRotation()
	{
		float targetRotation = Mathf.Lerp(Mathf.Pi * .25f, 0, launchRatio);
		armNode.Rotation = Vector3.Right * targetRotation;
	}

	private void OnEnteredCatapult()
	{
		currentState = CatapultState.Control;
		Character.StartExternal(this, playerPositionNode);
		Character.Effect.StartSpinFX();
		Character.Animator.StartSpin(3f);

		launchRatio = 1f;
		targetLaunchPower = 0f;
		launchPowerVelocity = 0f;

		tweener?.Kill();
		enterSFX.Play();
	}

	private void EjectPlayer(bool isCancel)
	{
		currentState = CatapultState.Eject;
		tweener = CreateTween();

		if (isCancel)
		{
			Character.Effect.StopSpinFX();
			tweener.TweenProperty(armNode, "rotation", Vector3.Zero, .2f * (1 - launchRatio)).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
			tweener.TweenCallback(new Callable(this, MethodName.CancelCatapult));
		}
		else
		{
			tweener.TweenProperty(armNode, "rotation", Vector3.Right * Mathf.Pi, .25f * (launchRatio + 1)).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
			tweener.TweenProperty(armNode, "rotation", Vector3.Zero, .4f).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
		}

		tweener.TweenCallback(new Callable(this, MethodName.StopProcessing));
	}

	public override void Activate()
	{
		// Cheat launch power slightly towards extremes
		launchRatio = Mathf.SmoothStep(0, 1, launchRatio);
		currentState = CatapultState.Disabled;
		base.Activate();
	}

	private void CancelCatapult()
	{
		if (currentState != CatapultState.Eject) return;
		currentState = CatapultState.Disabled;

		// Have the player jump out backwards
		Vector3 destination = (this.Back().RemoveVertical() * 2f) + (Vector3.Down * 2f);
		destination += Character.GlobalPosition;

		var settings = LaunchSettings.Create(Character.GlobalPosition, destination, 1f);
		settings.IsJump = true;
		Character.StartLauncher(settings);
		enterSFX.Play();
		EmitSignal(SignalName.PlayerExited);
	}

	public void OnEntered(Area3D a)
	{
		if (!a.IsInGroup("player")) return;
		StartProcessing();

		if (currentState != CatapultState.Disabled) return; // Already in the catapult
		currentState = CatapultState.Enter; // Start entering

		// Disable break skills
		Character.Skills.IsSpeedBreakEnabled = Character.Skills.IsTimeBreakEnabled = false;
		Character.Connect(CharacterController.SignalName.LaunchFinished, new Callable(this, MethodName.OnEnteredCatapult), (uint)ConnectFlags.OneShot);

		// Have the player jump into the catapult
		var settings = LaunchSettings.Create(Character.GlobalPosition, playerPositionNode.GlobalPosition, 2f);
		settings.IsJump = true;
		Character.StartLauncher(settings);
		EmitSignal(SignalName.PlayerEntered);
	}

	private void StartProcessing() => isProcessing = true;
	private void StopProcessing() => isProcessing = false;
}
