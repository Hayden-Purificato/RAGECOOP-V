﻿using System;
using GTA;
using GTA.Math;
using GTA.Native;
using RageCoop.Core;

namespace RageCoop.Client
{
    /// <summary>
    ///     A synchronized vehicle instance
    /// </summary>
    public partial class SyncedVehicle : SyncedEntity
    {
        internal override void Update()
        {

            // Check if all data available
            if (!IsReady || Owner == null) return;

            // Check existence
            if (MainVehicle == null || !MainVehicle.Exists() || MainVehicle.Model != Model)
                if (!CreateVehicle())
                    return;


            DisplayVehicle();
            // Skip update if no new sync message has arrived.
            if (!NeedUpdate) return;

            if (SteeringAngle != MainVehicle.SteeringAngle)
                MainVehicle.CustomSteeringAngle((float)(Math.PI / 180) * SteeringAngle);
            MainVehicle.ThrottlePower = ThrottlePower;
            MainVehicle.BrakePower = BrakePower;

            if (IsDead)
            {
                if (MainVehicle.IsDead) return;

                MainVehicle.Explode();
            }
            else
            {
                if (MainVehicle.IsDead)
                    WorldThread.Delay(() =>
                    {
                        if (MainVehicle.IsDead && !IsDead) MainVehicle.Repair();
                    }, 1000);
            }

            if (MainVehicle.IsOnFire)
            {
                if (!Flags.HasVehFlag(VehicleDataFlags.IsOnFire)) Function.Call(Hash.STOP_ENTITY_FIRE, MainVehicle);
            }
            else if (Flags.HasVehFlag(VehicleDataFlags.IsOnFire))
            {
                Function.Call(Hash.START_ENTITY_FIRE, MainVehicle);
            }

            if (EngineRunning != MainVehicle.IsEngineRunning) MainVehicle.IsEngineRunning = EngineRunning;

            if (LightsOn != MainVehicle.AreLightsOn) MainVehicle.AreLightsOn = LightsOn;

            if (HighBeamsOn != MainVehicle.AreHighBeamsOn) MainVehicle.AreHighBeamsOn = HighBeamsOn;

            if (IsAircraft)
            {
                if (LandingGear != (byte)MainVehicle.LandingGearState)
                    MainVehicle.LandingGearState = (VehicleLandingGearState)LandingGear;
            }
            else
            {
                if (MainVehicle.HasSiren && SireneActive != MainVehicle.IsSirenActive)
                    MainVehicle.IsSirenActive = SireneActive;

                if (HornActive)
                {
                    if (!_lastHornActive)
                    {
                        _lastHornActive = true;
                        MainVehicle.SoundHorn(99999);
                    }
                }
                else if (_lastHornActive)
                {
                    _lastHornActive = false;
                    MainVehicle.SoundHorn(1);
                }

                if (HasRoof && MainVehicle.RoofState != RoofState) MainVehicle.RoofState = RoofState;

                if (HasRocketBoost && Flags.HasFlag(VehicleDataFlags.IsRocketBoostActive) !=
                    MainVehicle.IsRocketBoostActive())
                    MainVehicle.SetRocketBoostActive(Flags.HasFlag(VehicleDataFlags.IsRocketBoostActive));
                if (HasParachute && Flags.HasFlag(VehicleDataFlags.IsParachuteActive) !=
                    MainVehicle.IsParachuteActive())
                    MainVehicle.SetParachuteActive(Flags.HasFlag(VehicleDataFlags.IsParachuteActive));
                if (IsSubmarineCar)
                {
                    if (Transformed)
                    {
                        if (!_lastTransformed)
                        {
                            _lastTransformed = true;
                            Function.Call(Hash._TRANSFORM_VEHICLE_TO_SUBMARINE, MainVehicle.Handle, false);
                        }
                    }
                    else if (_lastTransformed)
                    {
                        _lastTransformed = false;
                        Function.Call(Hash._TRANSFORM_SUBMARINE_TO_VEHICLE, MainVehicle.Handle, false);
                    }
                }
                else if (IsDeluxo)
                {
                    MainVehicle.SetDeluxoHoverState(IsDeluxoHovering);
                    if (IsDeluxoHovering) MainVehicle.SetDeluxoWingRatio(DeluxoWingRatio);
                }

                Function.Call(Hash.SET_VEHICLE_BRAKE_LIGHTS, MainVehicle.Handle, BrakeLightsOn);
            }

            MainVehicle.LockStatus = LockStatus;

            if (LastFullSynced >= LastUpdated)
            {
                if (Flags.HasVehFlag(VehicleDataFlags.Repaired)) MainVehicle.Repair();
                if (Colors != null && Colors != _lastVehicleColors)
                {
                    Function.Call(Hash.SET_VEHICLE_COLOURS, MainVehicle, Colors[0], Colors[1]);

                    _lastVehicleColors = Colors;
                }

                MainVehicle.EngineHealth = EngineHealth;
                if (Mods != null && !Mods.Compare(_lastVehicleMods))
                {
                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, MainVehicle, 0);

                    foreach (var mod in Mods) MainVehicle.Mods[(VehicleModType)mod.Key].Index = mod.Value;

                    _lastVehicleMods = Mods;
                }

                if (Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, MainVehicle) != LicensePlate)
                    Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, MainVehicle, LicensePlate);

                if (_lastLivery != Livery)
                {
                    Function.Call(Hash.SET_VEHICLE_LIVERY, MainVehicle, Livery);
                    _lastLivery = Livery;
                }

                MainVehicle.SetDamageModel(DamageModel);
            }

            LastUpdated = Main.Ticked;
        }

        private void DisplayVehicle()
        {
            _predictedPosition = Predict(Position);
            var current = MainVehicle.ReadPosition();
            var distSquared = current.DistanceToSquared(_predictedPosition);
            var cali = _predictedPosition - current;
            if (!IsTrain) { cali += 0.5f * (Velocity - MainVehicle.Velocity); }
            if (distSquared > 10 * 10)
            {
                MainVehicle.Position = _predictedPosition;
                MainVehicle.Velocity = Velocity;
                MainVehicle.Quaternion = Quaternion;
                return;
            }

            // Calibrate rotation
            var curQuat = MainVehicle.Quaternion;
            MainVehicle.ApplyForce(Quaternion * TopExtent - curQuat * TopExtent, TopExtent);
            MainVehicle.ApplyForce(Quaternion * FrontExtent - curQuat * FrontExtent, FrontExtent);

            // Calibrate position
            if (distSquared < 0.03 * 0.03) return;
            if (IsTrain || distSquared > 20 * 20) MainVehicle.Velocity = Velocity + cali;
            else MainVehicle.ApplyForce(cali);
        }

        private Vector3 GetCalibrationRotation()
        {
            var rot = Quaternion.LookRotation(Quaternion * Vector3.RelativeFront, Quaternion * Vector3.RelativeTop)
                .ToEulerAngles();
            var curRot = Quaternion.LookRotation(MainVehicle.ReadQuaternion() * Vector3.RelativeFront,
                MainVehicle.ReadQuaternion() * Vector3.RelativeTop).ToEulerAngles();

            var r = (rot - curRot).ToDegree();
            if (r.X > 180)
                r.X = r.X - 360;
            else if (r.X < -180) r.X = 360 + r.X;

            if (r.Y > 180)
                r.Y = r.Y - 360;
            else if (r.Y < -180) r.Y = 360 + r.Y;

            if (r.Z > 180)
                r.Z = r.Z - 360;
            else if (r.Z < -180) r.Z = 360 + r.Z;
            return r;
        }

        private bool CreateVehicle()
        {
            MainVehicle?.Delete();
            MainVehicle = Util.CreateVehicle(Model, Position);
            if (!Model.IsInCdImage)
                // GTA.UI.Notification.Show($"~r~(Vehicle)Model ({CurrentVehicleModelHash}) cannot be loaded!");
                return false;
            if (MainVehicle == null)
            {
                Model.Request();
                return false;
            }

            lock (EntityPool.VehiclesLock)
            {
                EntityPool.Add(this);
            }

            MainVehicle.Quaternion = Quaternion;
            if (MainVehicle.HasRoof) MainVehicle.RoofState = RoofState;
            foreach (var w in MainVehicle.Wheels) w.Fix();
            if (IsInvincible) MainVehicle.IsInvincible = true;
            SetUpFixedData();
            Model.MarkAsNoLongerNeeded();
            return true;
        }

        #region -- CONSTRUCTORS --

        /// <summary>
        ///     Create a local entity (outgoing sync)
        /// </summary>
        /// <param name="v"></param>
        internal SyncedVehicle(Vehicle v)
        {
            ID = EntityPool.RequestNewID();
            MainVehicle = v;
            MainVehicle.CanPretendOccupants = false;
            OwnerID = Main.LocalPlayerID;
            SetUpFixedData();
        }

        internal void SetUpFixedData()
        {
            if (MainVehicle == null) return;

            IsAircraft = MainVehicle.IsAircraft;
            IsMotorcycle = MainVehicle.IsMotorcycle;
            HasRocketBoost = MainVehicle.HasRocketBoost;
            HasParachute = MainVehicle.HasParachute;
            HasRoof = MainVehicle.HasRoof;
            IsSubmarineCar = MainVehicle.IsSubmarineCar;
            IsDeluxo = MainVehicle.Model == 1483171323;
            IsTrain = MainVehicle.IsTrain;
            var (rbl, ftr) = MainVehicle.Model.Dimensions;
            FrontExtent = new Vector3(0, ftr.Y, 0);
            TopExtent = new Vector3(0, 0, ftr.Z);
            LeftExtent = new Vector3(rbl.X, 0, 0);
            RightExtent = new Vector3(ftr.X, 0, 0);
        }

        /// <summary>
        ///     Create an empty VehicleEntity
        /// </summary>
        internal SyncedVehicle()
        {
        }

        internal SyncedVehicle(int id)
        {
            ID = id;
            LastSynced = Main.Ticked;
        }

        #endregion

        #region -- PEDALING --

        /*
         * Thanks to @oldnapalm.
         */

        private string PedalingAnimDict()
        {
            switch ((VehicleHash)Model)
            {
                case VehicleHash.Bmx:
                    return "veh@bicycle@bmx@front@base";
                case VehicleHash.Cruiser:
                    return "veh@bicycle@cruiserfront@base";
                case VehicleHash.Scorcher:
                    return "veh@bicycle@mountainfront@base";
                default:
                    return "veh@bicycle@roadfront@base";
            }
        }

        private string PedalingAnimName(bool fast)
        {
            return fast ? "fast_pedal_char" : "cruise_pedal_char";
        }

        private void StartPedalingAnim(bool fast)
        {
            MainVehicle.Driver?.Task.PlayAnimation(PedalingAnimDict(), PedalingAnimName(fast), 8.0f, -8.0f, -1,
                AnimationFlags.Loop | AnimationFlags.AllowRotation, 1.0f);
        }

        private void StopPedalingAnim(bool fast)
        {
            MainVehicle.Driver.Task.ClearAnimation(PedalingAnimDict(), PedalingAnimName(fast));
        }

        #endregion
    }
}