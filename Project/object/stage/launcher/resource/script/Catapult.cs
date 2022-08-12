using Godot;
using Project.Core;

namespace Project.Gameplay.Objects
{
	/// <summary>
	/// Launches the player a variable amount, using <see cref="launchPower"/> as the blend of close and far settings
	/// </summary>
	[Tool]
    public class Catapult : Spatial
    {
        [Export]
        public float closeDistance;
		[Export]
		public float closeMidHeight;
		[Export]
		public float closeEndHeight;
		[Export]
		public float farDistance;
		[Export]
		public float farMidHeight;
		[Export]
		public float farEndHeight;

		public CharacterController Character => CharacterController.instance;
		public InputManager.Controller Controller => Character.Controller;

		[Export(PropertyHint.Range, "0, 1")]
		public float launchPower; //0 <-> 1. Power of the shot. Exported for the editor
		private float launchPowerVelocity;
		private readonly float POWER_ADJUSTMENT_SPEED = .14f; //How fast to adjust the power
		private readonly float POWER_SMOOTHING_SPEED = .2f; //How fast to adjust the power
		private readonly float POWER_RESET_SPEED = .8f; //How fast to adjust the power

		[Export]
		public NodePath launchNode;
		public Spatial _launchNode;
		[Export]
		public NodePath armNode;
		public Spatial _armNode;
		public LaunchData GetLaunchData()
		{
			Vector3 launchPoint = GetLaunchPosition();
			float distance = Mathf.Lerp(closeDistance, farDistance, launchPower);
			float midHeight = Mathf.Lerp(closeMidHeight, farMidHeight, launchPower);
			float endHeight = Mathf.Lerp(closeEndHeight, farEndHeight, launchPower);
			return LaunchData.Create(launchPoint, launchPoint + this.Forward() * distance + Vector3.Up * endHeight, midHeight);
		}

		private readonly float CLOSE_WINDUP_ANGLE = -45f;
		private Vector3 GetLaunchPosition() => GlobalTranslation + this.Up() * 3.5f;

		private bool isEnteringCatapult;
		private bool isEjectingPlayer;
		private bool isControllingPlayer;

		public override void _EnterTree()
		{
			_armNode = GetNodeOrNull<Spatial>(armNode);
			_launchNode = GetNodeOrNull<Spatial>(launchNode);
		}

		public override void _PhysicsProcess(float _)
		{
			if (Engine.EditorHint)
			{
				if (_armNode != null)
					_armNode.RotationDegrees = Vector3.Right * Mathf.Lerp(CLOSE_WINDUP_ANGLE, 0, launchPower);

				return;
			}

			Vector3 targetRotation = Vector3.Zero;
			if(isControllingPlayer)
			{
				if(isEjectingPlayer)
				{

				}
				else //Update Controls
				{
					float targetLaunchPower = .5f - (Controller.verticalAxis.value * .5f);
					launchPower = ExtensionMethods.SmoothDamp(launchPower, targetLaunchPower, ref launchPowerVelocity, POWER_ADJUSTMENT_SPEED);
					targetRotation = Vector3.Right * Mathf.Lerp(CLOSE_WINDUP_ANGLE, 0, launchPower);

					if (Controller.jumpButton.wasPressed) //Cancel
						EjectPlayer(true);
					else if (Controller.actionButton.wasPressed) //Launch
						EjectPlayer(false);

					_armNode.RotationDegrees = _armNode.RotationDegrees.LinearInterpolate(targetRotation, POWER_SMOOTHING_SPEED);
				}
			}
			else
				_armNode.RotationDegrees = _armNode.RotationDegrees.LinearInterpolate(targetRotation, POWER_RESET_SPEED);
		}

		private void OnEnteredCatapult()
		{
			isControllingPlayer = true;
			isEnteringCatapult = false;
			Character.StartExternal(_launchNode, true);
			launchPower = 0f;
			launchPowerVelocity = 0f;
		}

		private void EjectPlayer(bool isCancel)
		{
			isEjectingPlayer = true;
			SceneTreeTween ejectTween = CreateTween().SetParallel(true).SetProcessMode(Tween.TweenProcessMode.Physics);

			if (isCancel)
			{
				float timeRatio = (1 - launchPower);
				ejectTween.TweenProperty(_armNode, "rotation_degrees", Vector3.Zero, .2f * timeRatio).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
				ejectTween.TweenCallback(this, nameof(CancelCatapult)).SetDelay(.2f * timeRatio);
			}
			else
			{
				ejectTween.TweenProperty(_armNode, "rotation_degrees", Vector3.Left * 90f, .4f).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Back);
				ejectTween.TweenCallback(this, nameof(LaunchPlayer)).SetDelay(.24f);
			}
		}

		private void LaunchPlayer()
		{
			isControllingPlayer = false;
			Character.Animator.ResetLocalRotation();
			Character.StartLauncher(GetLaunchData(), null, true);
		}

		private void CancelCatapult()
		{
			isControllingPlayer = false;
			Character.JumpTo(Character.GlobalTranslation + this.Back().RemoveVertical() * 2f + Vector3.Down * 2f, 1f);
		}

		public void PlayerEntered(Area a)
		{
			if (!a.IsInGroup("player")) return;

			if (isEnteringCatapult) return; //Already entering catapult

			isEjectingPlayer = false; //Reset
			isEnteringCatapult = true;

			Character.Soul.IsSpeedBreakEnabled = Character.Soul.IsTimeBreakEnabled = false; //Disable break skills
			Character.JumpTo(_launchNode.GlobalTranslation, 2f);
			Character.Connect(nameof(CharacterController.LauncherFinished), this, nameof(OnEnteredCatapult), null, (uint)ConnectFlags.Oneshot);
		}
	}
}
