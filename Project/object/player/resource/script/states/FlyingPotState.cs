using Godot;
using Project.Core;
using Project.Gameplay.Objects;

namespace Project.Gameplay;

public partial class FlyingPotState : PlayerState
{
	public FlyingPot Pot { get; set; }

	[Export]
	private PlayerState jumpState;

	private float flapSpeed = 1;
	private float flapTimer;
	private const float MaxSpeed = 12.0f;
	private const float MaxAngle = Mathf.Pi * .2f;
	private const float WingAcceleration = 4f;
	private const float FlapInterval = .5f; // How long is a single flap?
	private const float FlapAccelerationLength = .4f; // How long does a flap accelerate?

	public override void EnterState()
	{
		flapTimer = 0;
		Player.StartExternal(Pot, Pot.Root);
		Player.Animator.Visible = false;
		Player.MoveSpeed = Player.VerticalSpeed = 0;
	}

	public override void ExitState()
	{
		Player.CanJumpDash = true; // So the player isn't completely helpless
		Player.Skills.IsSpeedBreakEnabled = true;
		Player.Animator.Visible = true;
		Player.StopExternal();

		Pot = null;
	}

	public override PlayerState ProcessPhysics()
	{
		Pot.UpdateAngle(Player.Controller.InputHorizontal * MaxAngle);

		if (Player.Controller.IsJumpBufferActive)
		{
			Player.Controller.ResetJumpBuffer();
			Player.DisableAccelerationJump = true;
			LeavePot();
			return jumpState;
		}

		if (Player.Controller.IsActionBufferActive) // Move upwards
		{
			Player.Controller.ResetActionBuffer();

			Pot.Flap();
			flapSpeed = 1.5f + (flapTimer / FlapInterval);
			flapTimer = FlapInterval;
			return null;
		}

		UpdateFlap();
		UpdateAnimation();
		Pot.ApplyMovement();
		Player.UpdateExternalControl(); // Sync player object
		return null;
	}

	private void UpdateFlap()
	{
		if (Mathf.IsZeroApprox(flapTimer))
			return;

		if (Pot.Velocity < 0) // More responsive flap when falling
			Pot.Velocity *= .2f;

		flapTimer = Mathf.MoveToward(flapTimer, 0, PhysicsManager.physicsDelta);
		if (flapTimer < FlapAccelerationLength)
			return;

		// Accelerate
		Pot.Velocity += WingAcceleration;
		Pot.Velocity = Mathf.Min(Pot.Velocity, MaxSpeed);
	}

	private void UpdateAnimation()
	{
		flapSpeed = Mathf.Lerp(flapSpeed, 1, .1f);
		Pot.UpdateFlap(flapSpeed);
	}

	private void LeavePot()
	{
		Pot.Velocity = 0f; // Kill all velocity
		Pot.PlayExitFX();

		float angleRatio = Pot.Angle / MaxAngle;
		Player.MovementAngle = ExtensionMethods.CalculateForwardAngle(Pot.Back());
		Player.VerticalSpeed = Runtime.CalculateJumpPower(Player.Stats.JumpHeight);

		Player.Animator.JumpAnimation();
		Player.Animator.SnapRotation(Player.MovementAngle - (Mathf.Pi * angleRatio));
		Player.Animator.Visible = true;
		Player.StopExternal();
	}
}
