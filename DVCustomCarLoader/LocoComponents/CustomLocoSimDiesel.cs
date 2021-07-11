﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DV.JObjectExtstensions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CCL_GameScripts;
using DV.ServicePenalty;

namespace DVCustomCarLoader.LocoComponents
{
    public class CustomLocoSimDiesel : CustomLocoSimulation<SimParamsDiesel>, IServicePenaltyProvider
    {
		public float TotalFuelConsumed { get; private set; }

		//public SimParamsDiesel simParams;
		private DamageControllerDiesel dmgController;

		// Engine
		public bool engineOn;
		public bool goingForward;

		public SimComponent speed;
		public SimComponent throttle = new SimComponent("Throttle", 0f, 1f, 1f, 0f);
		public SimComponent throttlePower = new SimComponent("ThrottlePower", 0f, 1f, 0.1f, 0f);
		/// <summary>throttle - targetThrottle</summary>
		public SimComponent throttleToTargetDiff = new SimComponent("ThrottleTargetDifference", -1f, 1f, 1f, 0f);

		public SimComponent engineRPM = new SimComponent("EngineRPM", 0f, 1f, 0.01f, 0f);
		public SimComponent engineTemp = new SimComponent("EngineTemperature", 30f, 120f, 18f, 30f);

		public SimComponent fuel;
		public SimComponent oil;

		private const string TOTAL_FUEL_CONSUMED_SAVE_KEY = "fuelConsumed";

		public override IEnumerable<DebtTrackingInfo> GetDebtComponents()
        {
			return base.GetDebtComponents()
				.Concat(new[] 
				{
					new DebtTrackingInfo(this, new DebtComponent(0, ResourceType.EnvironmentDamageFuel)),
					new DebtTrackingInfo(this, new DebtComponent(fuel.value, ResourceType.Fuel)),
					new DebtTrackingInfo(this, new DebtComponent(oil.value, ResourceType.Oil))
				});
        }

        public override void ResetDebt( DebtComponent debt )
        {
			switch( debt.type )
			{
				case ResourceType.Fuel:
					debt.ResetComponent(fuel.value);
					break;

				case ResourceType.Oil:
					debt.ResetComponent(oil.value);
					break;

				case ResourceType.EnvironmentDamageFuel:
					debt.ResetComponent(TotalFuelConsumed);
					break;

				default:
					base.ResetDebt(debt);
					break;
			}
        }

        public override void UpdateDebtValue( DebtComponent debt )
        {
			switch( debt.type )
			{
				case ResourceType.Fuel:
					debt.UpdateEndValue(fuel.value);
					break;

				case ResourceType.Oil:
					debt.UpdateEndValue(oil.value);
					break;

				case ResourceType.EnvironmentDamageFuel:
					debt.UpdateStartValue(TotalFuelConsumed);
					break;

				default:
					base.UpdateDebtValue(debt);
					break;
			}
		}

        public override IEnumerable<PitStopRefillable> GetPitStopParameters()
        {
			return base.GetPitStopParameters()
				.Concat(new[]
				{
					new PitStopRefillable(this, ResourceType.Oil, oil),
					new PitStopRefillable(this, ResourceType.Fuel, fuel)
				});
        }

        public override JObject GetComponentsSaveData()
		{
			JObject jobject = new JObject();
			SimComponent.SaveComponentState(fuel, jobject);
			SimComponent.SaveComponentState(oil, jobject);
			SimComponent.SaveComponentState(sand, jobject);
			SimComponent.SaveComponentState(engineTemp, jobject);
			jobject.SetFloat("fuelConsumed", TotalFuelConsumed);
			return jobject;
		}

		protected override void InitComponents()
		{
			base.InitComponents();
			dmgController = GetComponent<DamageControllerDiesel>();

			speed = new SimComponent("Speed", 0f, simParams.MaxSpeed, 1f, 0f);
			fuel = new SimComponent("Fuel", 0f, simParams.FuelCapacity, 1200f, simParams.FuelCapacity);
			oil = new SimComponent("Oil", 0f, simParams.OilCapacity, 100f, simParams.OilCapacity);

			components = new SimComponent[]
			{
			fuel,
			oil,
			sand,
			sandFlow,
			engineTemp,
			engineRPM,
			throttlePower,
			throttle,
			throttleToTargetDiff,
			speed
			};
		}

		public override void LoadComponentsState( JObject stateData )
		{
			SimComponent.LoadComponentState(fuel, stateData);
			SimComponent.LoadComponentState(oil, stateData);
			SimComponent.LoadComponentState(sand, stateData);
			SimComponent.LoadComponentState(engineTemp, stateData);

			float? fuelConsumed = stateData.GetFloat(TOTAL_FUEL_CONSUMED_SAVE_KEY);
			if( fuelConsumed != null )
			{
				TotalFuelConsumed = fuelConsumed.Value;
				return;
			}

			Main.Error("No load data for fuelConsumed found!");
		}

		public override void ResetRefillableSimulationParams()
		{
			fuel.SetValue(fuel.max);
			oil.SetValue(oil.max);
			sand.SetValue(sand.max);
		}

        public override void ResetFuelConsumption()
        {
			TotalFuelConsumed = 0f;
		}

		private void SimulateEngineRPM( float delta )
		{
			float percentWarm = Mathf.InverseLerp(engineTemp.min, simParams.MaxPowerTemp, engineTemp.value);
			float warmupFactor = Mathf.Lerp(simParams.ColdEnginePowerFactor, 1f, percentWarm);

			float healthFactor = 1f - Mathf.InverseLerp(simParams.PerformanceDropDamageLevel, 1f, dmgController.engine.DamagePercentage);
			engineRPM.SetNextValue(throttlePower.value * throttle.value * warmupFactor * healthFactor);
			float throttleTgtDiff = Mathf.Round(throttleToTargetDiff.value * 1000f) / 1000f;

			// if tgt > throttle then throttleTgtDiff < 0

			// throttle down (tgt < current)
			if( (throttleTgtDiff > 0f) && (throttlePower.value > 0f) )
			{
				throttlePower.AddNextValue(-1f * Mathf.Abs(throttleTgtDiff) * simParams.ThrottleDownRate * delta);
				return;
			}

			// throttle up (tgt >= current)
			if( (throttleTgtDiff <= 0f) && (throttlePower.value < 1f) )
			{
				throttlePower.AddNextValue((1f - Mathf.Abs(throttleTgtDiff)) * simParams.ThrottleUpRate * delta);
			}
		}

		private void SimulateEngineTemp( float delta )
		{
			if( engineRPM.value > 0f && engineOn )
			{
				engineTemp.AddNextValue(engineRPM.value * simParams.TempGainPerRpm * delta);
			}
			if( engineOn && engineTemp.value < 52f )
			{
				engineTemp.AddNextValue(simParams.IdleTempGain * delta);
			}
			if( engineTemp.value > engineTemp.min )
			{
				engineTemp.AddNextValue(simParams.PassiveTempLoss * delta);
			}
		}

		private void SimulateFuel( float delta )
		{
			if( engineOn && fuel.value > 0f )
			{
				float multiplier = Mathf.Lerp(
					simParams.FuelConsumptionMin,
					simParams.FuelConsumptionMax,
					engineRPM.value);
				float rate = multiplier * simParams.FuelConsumptionBase * delta;

				TotalFuelConsumed += rate;
				fuel.AddNextValue(-1f * rate);
			}
		}

		private void SimulateOil( float delta )
		{
			if( engineRPM.value > 0f && oil.value > 0f )
			{
				oil.AddNextValue(-1f * engineRPM.value * simParams.OilConsumptionEngineRpm * delta);
			}
		}

		private void SimulateSand( float delta )
		{
			if( (sandOn && sand.value > 0f && sandFlow.value < sandFlow.max) || ((!sandOn || sand.value == 0f) && sandFlow.value > sandFlow.min) )
			{
				int num = (sandOn && sand.value > 0f) ? 1 : -1;
				sandFlow.AddNextValue(num * simParams.SandValveSpeed * delta);
			}
			if( sandFlow.value > 0f && sand.value > 0f )
			{
				sand.AddNextValue(-1f * sandFlow.value * simParams.SandMaxFlow * delta);
			}
		}

		protected override void SimulateTick( float delta )
		{
			InitNextValues();
			SimulateFuel(delta);
			SimulateOil(delta);
			SimulateSand(delta);
			SimulateEngineRPM(delta);
			SimulateEngineTemp(delta);
			SetValuesToNextValues();
		}
	}
}
