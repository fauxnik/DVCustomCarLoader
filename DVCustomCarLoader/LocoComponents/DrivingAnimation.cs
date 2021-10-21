﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CCL_GameScripts.Effects;

namespace DVCustomCarLoader.LocoComponents
{
    public class DrivingAnimation : MonoBehaviour
	{
		protected const string SPEED = "SpeedMultiplier";

		public float MaxWheelslipMultiplier = 8f;

		// Transforms
		public Transform[] transformsToRotate;
		public RotationAxis[] rotationAxes;
		public Vector3[] rotationLocalAxes;
		public float[] transformWheelRadii;

		protected float[] transformRevSpeed;
		protected float[] transformCircumferences;

		// Animation
		public Animator[] animators;
		public float[] startTimeOffsets;
		public float[] animatorWheelRadii;

		protected float[] animatorRevSpeed;
		protected float[] animatorCircumferences;

		protected TrainCar trainCar;
		protected LocoControllerBase loco;
		protected float curVelocity;

		protected static Vector3 WorldToLocalAxis(RotationAxis axis)
        {
			if (axis == RotationAxis.XAxis) return Vector3.right;
			if (axis == RotationAxis.YAxis) return Vector3.up;
			return Vector3.forward;
        }

		protected void Start()
		{
			trainCar = GetComponent<TrainCar>();

			// get circumferences
			transformCircumferences = transformWheelRadii.Select(r => (r * 2f * Mathf.PI)).ToArray();
			animatorCircumferences = animatorWheelRadii.Select(r => (r * 2f * Mathf.PI)).ToArray();

			transformRevSpeed = new float[transformsToRotate.Length];
			animatorRevSpeed = new float[animators.Length];

			rotationLocalAxes = rotationAxes.Select(WorldToLocalAxis).ToArray();

			bool anyMoving = false;
			foreach (Bogie bogie in trainCar.Bogies)
			{
				bogie.StoppedOnSlope += OnBogieStopped;
				anyMoving |= !bogie.isStoppedOnSlope;
			}
			enabled = anyMoving;

			Main.Log($"DrivingAnimation on  Enabled: {enabled}");

			loco = gameObject.GetComponent<LocoControllerBase>();

			for (int i = 0; i < animators.Length; i++)
			{
				animators[i].Play(animators[i].GetCurrentAnimatorStateInfo(0).shortNameHash, 0, startTimeOffsets[i]);
			}
		}

		private void OnBogieStopped(bool stopped)
		{
			if (enabled != stopped)
			{
				// no change in stopped state
				return;
			}

			foreach (var bogie in trainCar.Bogies)
			{
				if (bogie.isStoppedOnSlope != stopped)
				{
					// not all bogies in same state
					return;
				}
			}

			// final bogie to start/stop triggers state switch
			enabled = !stopped;
			Update();
		}

		private void SetRevSpeeds(float[] c, float[] dest)
        {
			for (int i = 0; i < c.Length; i++)
			{
				dest[i] = curVelocity / c[i];
				if (loco && (loco.drivingForce.wheelslip > 0))
                {
					dest[i] = Mathf.Lerp(dest[i], Mathf.Sign(loco.reverser) * MaxWheelslipMultiplier, loco.drivingForce.wheelslip);
				}
            }
		}

		private void UpdateTransformRotations()
        {
			for (int i = 0; i < transformsToRotate.Length; i++)
            {
				transformsToRotate[i].Rotate(rotationLocalAxes[i], transformRevSpeed[i] * 360f * Time.deltaTime, Space.Self);
			}
        }

		private void UpdateAnimatorSpeeds()
        {
			for (int i = 0; i < animators.Length; i++)
            {
				animators[i].SetFloat(SPEED, animatorRevSpeed[i]);
			}
        }

		protected void Update()
		{
			curVelocity = trainCar.GetForwardSpeed();
			if (Mathf.Abs(curVelocity) < 0.005f)
            {
				curVelocity = 0;
            }

			SetRevSpeeds(transformCircumferences, transformRevSpeed);
			UpdateTransformRotations();

			SetRevSpeeds(animatorCircumferences, animatorRevSpeed);
			UpdateAnimatorSpeeds();
		}
	}
}
