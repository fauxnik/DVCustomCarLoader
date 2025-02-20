﻿using CCL_GameScripts.CabControls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace DVCustomCarLoader.LocoComponents.Steam
{
    public class CustomSteamSaveState : CustomLocoSaveState<CustomLocoSimSteam, CustomDamageControllerSteam, CustomLocoControllerSteam>
    {

    }

    public class CustomLocoControllerSteam :
        CustomLocoController<CustomLocoSimSteam, CustomDamageControllerSteam, CustomLocoSimEventsSteam, CustomSteamSaveState>
    {
        public TrainCar LastTender { get; protected set; }

        #region Events

        protected override void Awake()
        {
            base.Awake();
            AddWatchables();
        }

        protected override void Start()
        {
            base.Start();
            train.brakeSystem.compressorRunning = true;
            train.brakeSystem.compressorProductionRate = 0;

            train.rearCoupler.Coupled += OnRearCouple;
            train.rearCoupler.Uncoupled += OnRearUncouple;

            if (!VRManager.IsVREnabled())
            {
                var keyboardCtrl = gameObject.AddComponent<SteamKeyboardInput>();
                keyboardCtrl.control = this;
                keyboardCtrl.Ctrl = this;
            }

            maxPowerPressure = sim.boilerPressure.max / 3f;
        }

        private void OnRearCouple(object _, CoupleEventArgs e)
        {
            if (!sim.simParams.AllowAnyTenderConnection) return;

            var attachedCar = e.otherCoupler.train;
            if (CarTypes.IsTender(attachedCar.carType))
            {
                var customTender = attachedCar.GetComponent<CustomTenderSimulation>();
                if (customTender)
                {
                    LastTender = attachedCar;

                    if (sim.simParams.AllowTenderWater)
                    {
                        sim.tenderWater = customTender.tenderWater;
                    }

                    if (sim.simParams.AllowTenderFuel && (sim.FuelType == customTender.FuelType))
                    {
                        sim.tenderFuel = customTender.tenderFuel;
                    }
                    sim.UpdateComponentReferences();
                    return;
                }

                var baseTender = attachedCar.GetComponent<TenderSimulation>();
                if (baseTender)
                {
                    LastTender = attachedCar;

                    if (sim.simParams.AllowTenderWater)
                    {
                        sim.tenderWater = baseTender.tenderWater;
                    }

                    if (sim.simParams.AllowTenderFuel && (sim.FuelType == ResourceType.Coal))
                    {
                        sim.tenderFuel = baseTender.tenderCoal;
                    }
                    sim.UpdateComponentReferences();
                    return;
                }
            }
        }

        private void OnRearUncouple(object _, UncoupleEventArgs e)
        {
            if (!sim.simParams.AllowAnyTenderConnection) return;

            if (CarTypes.IsTender(e.otherCoupler.train.carType))
            {
                sim.tenderWater.SetValue(sim.tenderWater.nextValue);
                sim.tenderFuel.SetValue(sim.tenderFuel.nextValue);
                sim.SetInternalFuelComponents();
                sim.UpdateComponentReferences();
            }
        }

        public override void Update()
        {
            base.Update();
            // TODO: Whistle
            sim.speed.SetValue(GetSpeedKmH());

            float compressorRate = (sim.boilerPressure.value - train.brakeSystem.mainReservoirPressure) * sim.simParams.AirCompressorRate;
            train.brakeSystem.compressorProductionRate = Mathf.Max(compressorRate, 0);
        }

        #endregion

        //================================================================================
        #region Simulation

        public override void SetNeutralState()
        {
            sim.fireOn.SetValue(0f);
            SetSanders(0f);
            sim.sandFlow.SetValue(0);
            SetInjector(0f);
            SetThrottle(0f);
            SetReverser(0f);
            SetBrake(0f);
            SetIndependentBrake(1f);
        }

        public float GetTotalPowerForcePercentage()
        {
            //bool dirMatchesReverser = (GetForwardSpeed() * reverser) >= 0;
            //float torqueMultiplier = dirMatchesReverser ? tractionTorqueCurve.Evaluate(GetSpeedKmH()) : 1f;
            return sim.power.value; // * torqueMultiplier;
        }

        public override float GetTractionForce()
        {
            bool dirMatchesReverser = (GetForwardSpeed() * reverser) >= 0;
            float calcSpeed = dirMatchesReverser ? sim.speed.value : 0;
            return sim.power.value * sim.GetMaxTractiveEffort(calcSpeed); //GetTotalPowerForcePercentage() * tractionTorqueMult;
        }

        public override float GetTotalAppliedForcePerBogie()
        {
            if (reverser == 0) return 0;
            return Mathf.Sign(reverser) * GetTractionForce() / train.Bogies.Length;
        }

        public void AddCoalChunk() => sim.AddCoalChunk();

        #endregion

        #region Cab Controls

        public override void SetThrottle(float throttle)
        {
            base.SetThrottle(throttle);
            sim.regulator.SetValue(throttle);
        }

        public override void SetReverser(float position)
        {
            base.SetReverser(position);
            sim.cutoff.SetValue(Mathf.Abs(position));
        }

        public override void SetSanders(float value)
        {
            sim.sandValve.SetValue(value);
            base.SetSanders(value);
            EventManager.Dispatch(this, SimEventType.SandDeploy, value);
        }

        public float GetSanderValve()
        {
            return sim.sandValve.value;
        }

        public override float GetSandersFlow()
        {
            return sim.sandFlow.value * sim.simParams.SandMaxFlow;
        }

        public float GetBlower() => sim.blower.value;
        public void SetBlower(float value) => sim.blower.SetValue(value);

        public float GetDamper() => sim.damper.value;
        public void SetDamper(float value) => sim.damper.SetValue(value);

        public float GetInjector() => sim.injector.value;
        public void SetInjector(float value) => sim.injector.SetValue(value);

        public float GetWaterDump() => sim.waterDump.value;
        public void SetWaterDump(float value) => sim.waterDump.SetValue(value);

        public float GetSteamDump() => sim.steamReleaser.value;
        public void SetSteamDump(float value) => sim.steamReleaser.SetValue(value);

        protected float whistleValue;
        public float GetWhistle() => whistleValue;
        public void SetWhistle(float value) => whistleValue = value;

        public float GetFireDoor() => sim.fireDoorOpen.value;
        public void SetFireDoor(float value) => sim.fireDoorOpen.SetValue(value);

        public float GetStoker() => sim.autoFuelFeed.value;
        public void SetStoker(float value) => sim.autoFuelFeed.SetValue(value);

        public float GetFireOn() => sim.fireOn.value;
        public void SetFireOn(float value) => sim.fireOn.SetValue(value);

        public void SetFireOut(float value)
        {
            if (value > 0)
            {
                sim.TryExtinguishFire();
            }
        }

        public override bool AcceptsControlOfType(CabInputType inputType)
        {
            return inputType.EqualsOneOf(
                CabInputType.Sand,
                CabInputType.Blower,
                CabInputType.Damper,
                CabInputType.Injector,
                CabInputType.WaterDump,
                CabInputType.SteamDump,
                CabInputType.Whistle,
                CabInputType.FireDoor,
                CabInputType.Stoker,
                CabInputType.FireOut
            ) || base.AcceptsControlOfType(inputType);
        }

        public override void RegisterControl(CabInputRelay inputRelay)
        {
            switch (inputRelay.Binding)
            {
                case CabInputType.Sand:
                    inputRelay.SetIOHandlers(SetSanders, GetSanderValve);
                    break;

                case CabInputType.Blower:
                    inputRelay.SetIOHandlers(SetBlower, GetBlower);
                    break;

                case CabInputType.Damper:
                    inputRelay.SetIOHandlers(SetDamper, GetDamper);
                    break;

                case CabInputType.Injector:
                    inputRelay.SetIOHandlers(SetInjector, GetInjector);
                    break;

                case CabInputType.WaterDump:
                    inputRelay.SetIOHandlers(SetWaterDump, GetWaterDump);
                    break;

                case CabInputType.SteamDump:
                    inputRelay.SetIOHandlers(SetSteamDump, GetSteamDump);
                    break;

                case CabInputType.Whistle:
                    inputRelay.SetIOHandlers(SetWhistle, () => 0);
                    break;

                case CabInputType.FireDoor:
                    inputRelay.SetIOHandlers(SetFireDoor, GetFireDoor);
                    break;

                case CabInputType.Stoker:
                    inputRelay.SetIOHandlers(SetStoker, GetStoker);
                    break;

                case CabInputType.FireOut:
                    inputRelay.SetIOHandlers(SetFireOut);
                    break;

                default:
                    base.RegisterControl(inputRelay);
                    break;
            }
        }

        #endregion

        //================================================================================
        #region Watchable Values

        private float maxPowerPressure;
        protected override float AccessoryPowerLevel
        {
            get
            {
                if (sim.boilerPressure.value > maxPowerPressure) return 1;
                return Mathf.InverseLerp(0, maxPowerPressure, sim.boilerPressure.value);
            }
        }

        private void AddWatchables()
        {
            _watchables.AddNew(this, SimEventType.Sand, sim.sand);
            _watchables.AddNew(this, SimEventType.Cutoff, () => reverser);
            _watchables.AddNew(this, SimEventType.BoilerPressure, sim.boilerPressure);
            _watchables.AddNew(this, SimEventType.WaterLevel, sim.boilerWater);
            _watchables.AddNew(this, SimEventType.FireTemp, sim.temperature);
            _watchables.AddNew(this, SimEventType.FireboxLevel, sim.fireboxFuel);
            _watchables.AddNew(this, SimEventType.InjectorFlow, sim.injector);

            _watchables.AddNew(this, SimEventType.Fuel, sim.tenderFuel);
            _watchables.AddNew(this, SimEventType.WaterReserve, sim.tenderWater);
        }

        public float FireboxLevel => sim.fireboxFuel.value;
        public float FireTemp => sim.temperature.value;
        public float MaxFireTemp => sim.temperature.max;
        public float FireTempPercent => sim.temperature.value / sim.temperature.max;

        public float BlowerFlow => sim.GetBlowerFlowPercent();
        public float DraftFlow => sim.GetDraftFlowPercent();

        public float BoilerWater => sim.boilerWater.value;
        public float MaxBoilerWater => sim.boilerWater.max;
        public float BoilerWaterPercent => sim.boilerWater.value / sim.boilerWater.max;

        public float BoilerPressure => sim.boilerPressure.value;
        public float MaxBoilerPressure => sim.boilerPressure.max;
        public float BoilerPressurePercent => sim.boilerPressure.value / sim.boilerPressure.max;

        public float FuelConsumptionRate => sim.fuelConsumptionRate;
        public float MaxFuelConsumptionRate => sim.maxFuelConsumptionRate;
        public float BurnRatePercent => FuelConsumptionRate / MaxFuelConsumptionRate;

        public float TenderWater => sim.tenderWater.value;

        public float SafetyValve => sim.safetyPressureValve.value;
        public float PressureLeak => sim.pressureLeakMultiplier;

        #endregion
    }
}
