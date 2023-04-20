using Godot;
using Project.Core;

namespace Project.Gameplay.Triggers
{
	/// <summary>
	/// Makes the player do a 90 degree turn when entering fast enough.
	/// </summary>
	[Tool]
	public partial class DriftTrigger : Area3D
	{
		[Signal]
		public delegate void DriftStartedEventHandler();
		[Signal]
		public delegate void DriftCompletedEventHandler();

		[Export]
		private bool isRightTurn; // Which way is the corner?

		//Public for the editor
		public Vector3 EndPosition => MiddlePosition + ExitDirection * slideDistance;
		public Vector3 MiddlePosition => GlobalPosition + this.Back() * slideDistance;
		public Vector3 ExitDirection => this.Right() * (isRightTurn ? 1 : -1);

		private float entrySpeed; // Entry speed
		private CharacterController Character => CharacterController.instance;

		/// <summary> Is this drift trigger currently processing? </summary>
		private bool isProcessing;
		/// <summary> Did the player already press the action button and fail? </summary>
		private bool wasDriftAttempted;
		/// <summary> Was this drift successful? </summary>
		private bool wasDriftSuccessful;

		/// <summary> For smooth damp </summary>
		private Vector3 driftVelocity;
		/// <summary> Positional smoothing </summary>
		private const float DRIFT_SMOOTHING = .25f;
		/// <summary> How generous the input window is -How wide the road is- (Due to player's decceleration, it's harder to get an early drift.) </summary>
		private const float INPUT_WINDOW_DISTANCE = 1f;

		/// <summary> How far to slide. </summary>
		[Export(PropertyHint.Range, "1, 10")]
		private int slideDistance = 10;
		/// <summary> Entrance speed (ratio) required to start a drift. </summary>
		private const float ENTRANCE_SPEED_RATIO = .9f;

		[ExportGroup("Components")]
		[Export]
		private AudioStreamPlayer sfx;
		[Export]
		private LockoutResource lockout;
		private float startingVolume;
		private bool isFadingSFX;
		private float MIN_STARTING_VOLUME = -6f; //SFX volume when player enters slowly
		/// <summary> Delay animation state reset for this amount of time. </summary>
		private float driftAnimationTimer;
		/// <summary> Length of animation when player doesn't do anything. </summary>
		private const float DEFAULT_ANIMATION_LENGTH = .2f;
		/// <summary> Length of animation when player succeeds. </summary>
		private const float LAUNCH_ANIMATION_LENGTH = .4f;
		/// <summary> Length of animation when player faceplants. </summary>
		private const float FAIL_ANIMATION_LENGTH = .8f;

		public override void _PhysicsProcess(double _)
		{
			if (!isProcessing)
			{
				if (isFadingSFX)
					isFadingSFX = SoundManager.FadeAudioPlayer(sfx);

				if (driftAnimationTimer > 0)
				{
					driftAnimationTimer = Mathf.MoveToward(driftAnimationTimer, 0, PhysicsManager.physicsDelta);

					if (Mathf.IsZeroApprox(driftAnimationTimer))
						Character.Animator.StopDrift();
				}

				return; // Inactive
			}

			UpdateDrift();
		}

		private bool IsDriftValid() // Checks whether the player is in a state where a drift is possible
		{
			if (Character.IsMovingBackward) return false; // Can't drift backwards
			if (!Character.IsOnGround || Character.GroundSettings.GetSpeedRatio(Character.MoveSpeed) < ENTRANCE_SPEED_RATIO) return false; //In air/too slow
			if (Character.MovementState == CharacterController.MovementStates.External) return false; //Player is already busy

			// Check for any obstructions
			RaycastHit hit = Character.CastRay(Character.CenterPosition, Character.PathFollower.Forward() * slideDistance, Runtime.Instance.environmentMask);
			if (hit)
				return false;

			return true; // Valid drift
		}

		private void StartDrift() // Initialize drift
		{
			isProcessing = true;

			entrySpeed = Character.MoveSpeed;
			driftVelocity = Vector3.Zero;

			wasDriftAttempted = false;
			wasDriftSuccessful = false;

			//Reset sfx volume
			float speedRatio = (Character.GroundSettings.GetSpeedRatioClamped(entrySpeed)) - ENTRANCE_SPEED_RATIO / (1 - ENTRANCE_SPEED_RATIO);
			startingVolume = Mathf.Lerp(MIN_STARTING_VOLUME, 0, speedRatio);
			isFadingSFX = false;
			sfx.VolumeDb = startingVolume;
			sfx.Play();

			driftAnimationTimer = DEFAULT_ANIMATION_LENGTH;
			Character.StartExternal(this); // For future reference, this is wheere speedbreak gets disabled
			Character.Animator.ExternalAngle = Character.MovementAngle;
			Character.Animator.StartDrift(isRightTurn);
			Character.Connect(CharacterController.SignalName.Knockback, new Callable(this, MethodName.CompleteDrift));

			EmitSignal(SignalName.DriftStarted);
		}

		private void UpdateDrift()
		{
			Vector3 targetPosition = MiddlePosition + this.Back() * INPUT_WINDOW_DISTANCE;

			// Process drift
			float distance = Character.GlobalPosition.Flatten().DistanceTo(targetPosition.Flatten());
			Character.GlobalPosition.SmoothDamp(targetPosition, ref driftVelocity, DRIFT_SMOOTHING, entrySpeed);
			Character.Velocity = driftVelocity;
			Character.MoveAndSlide();
			Character.UpDirection = Character.PathFollower.Up(); // Use pathfollower's up direction when drifting
			Character.UpdateExternalControl();
			Character.UpdateOrientation(true);

			// Fade out sfx based on distance
			float volume = distance / slideDistance;
			sfx.VolumeDb = Mathf.SmoothStep(startingVolume, -80f, volume);

			bool isAttemptingDrift = (Input.IsActionJustPressed("button_action") && Character.Skills.isManualDriftEnabled) ||
				(!Character.Skills.isManualDriftEnabled && distance <= INPUT_WINDOW_DISTANCE);

			if (!wasDriftAttempted)
			{
				if (Input.IsActionJustPressed("button_jump")) //Allow character to jump out of drift at any time
				{
					driftAnimationTimer = 0;
					CompleteDrift();

					ApplyBonus();
					Character.Jump();
					Character.MoveSpeed = driftVelocity.Length(); //Keep speed from drift
				}
				else if (isAttemptingDrift)
				{
					if (!Character.Skills.isManualDriftEnabled || distance <= INPUT_WINDOW_DISTANCE * 2f) //Successful drift
					{
						wasDriftAttempted = true;
						wasDriftSuccessful = true;
						ApplyBonus();

						Character.Animator.LaunchDrift();
						driftAnimationTimer = LAUNCH_ANIMATION_LENGTH;

						Character.AddLockoutData(lockout); //Apply lockout
						CompleteDrift();
					}
					else //Too early! Fail drift attempt and play a special animation
					{
						wasDriftAttempted = true;
						driftAnimationTimer = FAIL_ANIMATION_LENGTH;
						ApplyBonus();
						CompleteDrift();
					}
				}
				else if (distance < .1f)
				{
					Character.MoveSpeed = 0f; //Reset Movespeed
					CompleteDrift();
				}
			}

			Character.PathFollower.Resync(); //Resync
		}

		private void CompleteDrift()
		{
			isFadingSFX = true; //Fade sound effect
			isProcessing = false;

			//Turn 90 degrees
			Character.MovementAngle = Character.CalculateForwardAngle(ExitDirection);
			Character.Animator.ExternalAngle = Character.MovementAngle;

			Character.ResetMovementState();
			Character.Animator.ResetState(0.4f);

			if (Character.IsConnected(CharacterController.SignalName.Knockback, new Callable(this, MethodName.CompleteDrift)))
				Character.Disconnect(CharacterController.SignalName.Knockback, new Callable(this, MethodName.CompleteDrift));

			EmitSignal(SignalName.DriftCompleted);
		}

		private bool wasBonusApplied; //Was this corner attempted before?
		private void ApplyBonus()
		{
			if (wasBonusApplied) return; //Bonus was already applied

			wasBonusApplied = true;
		}

		public void OnEntered(Area3D _)
		{
			if (!IsDriftValid())
			{
				ApplyBonus(); //Invalid drift, skip bonus
				return;
			}

			StartDrift(); //Drift started successfully
		}
	}
}