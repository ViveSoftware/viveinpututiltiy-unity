//========= Copyright 2016-2020, HTC Corporation. All rights reserved. ===========

using System;
using HTC.UnityPlugin.Utility;
using Object = UnityEngine.Object;
#if VIU_OCULUSVR
using UnityEngine;
using HTC.UnityPlugin.Vive;
using HTC.UnityPlugin.Vive.OculusVRExtension;
#if UNITY_2017_2_OR_NEWER
using UnityEngine.XR;
#else
using XRDevice = UnityEngine.VR.VRDevice;
using XRSettings = UnityEngine.VR.VRSettings;
#endif
#if VIU_XR_GENERAL_SETTINGS
using UnityEngine.XR.Management;
using UnityEngine.SpatialTracking;
#endif
#endif

namespace HTC.UnityPlugin.VRModuleManagement
{
    public partial class VRModule : SingletonBehaviour<VRModule>
    {
        public static readonly bool isOculusVRPluginDetected =
#if VIU_OCULUSVR
            true;
#else
            false;
#endif
        public static readonly bool isOculusVRDesktopSupported =
#if VIU_OCULUSVR_DESKTOP_SUPPORT
            true;
#else
            false;
#endif
        public static readonly bool isOculusVRAndroidSupported =
#if VIU_OCULUSVR_ANDROID_SUPPORT
            true;
#else
            false;
#endif

        public static readonly bool isOculusVRAvatarSupported =
#if VIU_OCULUSVR_AVATAR
            true;
#else
            false;
#endif
    }

    public sealed class OculusVRModule : VRModule.ModuleBase
    {
        private class Skeleton
        {
            private const string LeftHandSkeletonName = "LeftHandSkeleton";
            private const string RightHandSkeletonName = "RightHandSkeleton";

            private static readonly Quaternion WristFixupRotation = new Quaternion(0.0f, 1.0f, 0.0f, 0.0f);
            private static readonly Quaternion LeftHandOpenXRFixRotation = Quaternion.Euler(0.0f, 90.0f, 180.0f);
            private static readonly Quaternion RightHandOpenXRFixRotation = Quaternion.Euler(0.0f, -90.0f, 0.0f);

            public readonly OVRPlugin.SkeletonType Handness;
            public readonly Transform Root;
            public readonly Transform[] Bones = new Transform[(int) OVRPlugin.BoneId.Max];

            public Skeleton(OVRPlugin.SkeletonType handness)
            {
                Handness = handness;

                string name = Handness == OVRPlugin.SkeletonType.HandLeft ? LeftHandSkeletonName : RightHandSkeletonName;
                Root = new GameObject(name).transform;
                OVRPlugin.Skeleton ovrSkeleton;
                if (OVRPlugin.GetSkeleton(Handness, out ovrSkeleton))
                {
                    for (int i = 0; i < (int) OVRSkeleton.BoneId.Max; i++)
                    {
                        OVRSkeleton.BoneId id = (OVRSkeleton.BoneId) ovrSkeleton.Bones[i].Id;
                        GameObject boneObj = new GameObject(id.ToString());
                        Bones[i] = boneObj.transform;

                        Vector3 pos = ovrSkeleton.Bones[i].Pose.Position.FromFlippedXVector3f();
                        Quaternion rot = ovrSkeleton.Bones[i].Pose.Orientation.FromFlippedXQuatf();
                        Bones[i].localPosition = pos;
                        Bones[i].localRotation = rot;
                    }

                    for (int i = 0; i < (int) OVRSkeleton.BoneId.Max; i++)
                    {
                        int parentIndex = ovrSkeleton.Bones[i].ParentBoneIndex;
                        if (parentIndex == (int) OVRPlugin.BoneId.Invalid)
                        {
                            Bones[i].SetParent(Root);
                        }
                        else
                        {
                            Bones[i].SetParent(Bones[parentIndex]);
                        }
                    }
                }
            }

            public void Update(OVRPlugin.HandState handState)
            {
                Root.localPosition = handState.RootPose.Position.FromFlippedZVector3f();
                Root.localRotation = handState.RootPose.Orientation.FromFlippedZQuatf();
                Root.localScale = new Vector3(handState.HandScale, handState.HandScale, handState.HandScale);

                OVRPlugin.Skeleton ovrSkeleton;
                if (OVRPlugin.GetSkeleton(Handness, out ovrSkeleton))
                {
                    for (int i = 0; i < (int) OVRSkeleton.BoneId.Max; i++)
                    {
                        Vector3 pos = ovrSkeleton.Bones[i].Pose.Position.FromFlippedXVector3f();
                        Quaternion rot = handState.BoneRotations[i].FromFlippedXQuatf();
                        Bones[i].localPosition = pos;
                        Bones[i].localRotation = rot;

                        if (i == (int) OVRSkeleton.BoneId.Hand_WristRoot)
                        {
                            Bones[i].localRotation *= WristFixupRotation;
                        }
                    }
                }
            }

            public Quaternion GetOpenXRRotation(OVRPlugin.BoneId boneId)
            {
                Quaternion fixQuat = Handness == OVRPlugin.SkeletonType.HandLeft ? LeftHandOpenXRFixRotation : RightHandOpenXRFixRotation;
                return Bones[(int) boneId].rotation * fixQuat;
            }
        }

        public override int moduleOrder { get { return (int)DefaultModuleOrder.OculusVR; } }

        public override int moduleIndex { get { return (int)VRModuleSelectEnum.OculusVR; } }

        public const string OCULUS_XR_LOADER_NAME = "Oculus Loader";
        public const string OCULUS_XR_LOADER_CLASS_NAME = "OculusLoader";

#if VIU_OCULUSVR
        private class CameraCreator : VRCameraHook.CameraCreator
        {
            public override bool shouldActive { get { return s_moduleInstance == null ? false : s_moduleInstance.isActivated; } }

            public override void CreateCamera(VRCameraHook hook)
            {
#if UNITY_2019_3_OR_NEWER && VIU_XR_GENERAL_SETTINGS
                if (hook.GetComponent<TrackedPoseDriver>() == null)
                {
                    hook.gameObject.AddComponent<TrackedPoseDriver>();
                }
#endif
            }
        }
#endif

#if VIU_OCULUSVR_1_32_0_OR_NEWER || VIU_OCULUSVR_1_36_0_OR_NEWER || VIU_OCULUSVR_1_37_0_OR_NEWER
        private class RenderModelCreator : RenderModelHook.RenderModelCreator
        {
            private uint m_index = INVALID_DEVICE_INDEX;
            private VIUOculusVRRenderModel m_model;

            public override bool shouldActive { get { return s_moduleInstance == null ? false : s_moduleInstance.isActivated; } }

            public override void UpdateRenderModel()
            {
                if (!ChangeProp.Set(ref m_index, hook.GetModelDeviceIndex())) { return; }

                if (VRModule.IsValidDeviceIndex(m_index))
                {
                    // create object for render model
                    if (m_model == null)
                    {
                        var go = new GameObject("Model");
                        go.transform.SetParent(hook.transform, false);
                        m_model = go.AddComponent<VIUOculusVRRenderModel>();
                    }

                    // set render model index
                    m_model.gameObject.SetActive(true);
                    m_model.shaderOverride = hook.overrideShader;
#if VIU_OCULUSVR_1_32_0_OR_NEWER || VIU_OCULUSVR_1_36_0_OR_NEWER
                    m_model.gameObject.AddComponent(System.Type.GetType("OvrAvatarTouchController"));
#endif
                    m_model.SetDeviceIndex(m_index);
                }
                else
                {
                    // deacitvate object for render model
                    if (m_model != null)
                    {
                        m_model.gameObject.SetActive(false);
                    }
                }
            }

            public override void CleanUpRenderModel()
            {
                if (m_model != null)
                {
                    Object.Destroy(m_model.gameObject);
                    m_model = null;
                    m_index = INVALID_DEVICE_INDEX;
                }
            }
        }

        private static OculusVRModule s_moduleInstance;
#endif

#if VIU_OCULUSVR
        public const int VALID_NODE_COUNT = 7;

        private static readonly OVRPlugin.Node[] s_index2node;
        private static readonly uint[] s_node2index;
        private static readonly VRModuleDeviceClass[] s_node2class;
        
        private OVRPlugin.TrackingOrigin m_prevTrackingSpace;
        private Skeleton LeftHandSkeleton;
        private Skeleton RightHandSkeleton;

        static OculusVRModule()
        {
            s_index2node = new OVRPlugin.Node[VALID_NODE_COUNT];
            for (int i = 0; i < s_index2node.Length; ++i) { s_index2node[i] = OVRPlugin.Node.None; }
            s_index2node[0] = OVRPlugin.Node.Head;
            s_index2node[1] = OVRPlugin.Node.HandLeft;
            s_index2node[2] = OVRPlugin.Node.HandRight;
            s_index2node[3] = OVRPlugin.Node.TrackerZero;
            s_index2node[4] = OVRPlugin.Node.TrackerOne;
            s_index2node[5] = OVRPlugin.Node.TrackerTwo;
            s_index2node[6] = OVRPlugin.Node.TrackerThree;

            s_node2index = new uint[(int)OVRPlugin.Node.Count];
            for (int i = 0; i < s_node2index.Length; ++i) { s_node2index[i] = INVALID_DEVICE_INDEX; }
            s_node2index[(int)OVRPlugin.Node.Head] = 0;
            s_node2index[(int)OVRPlugin.Node.HandLeft] = 1;
            s_node2index[(int)OVRPlugin.Node.HandRight] = 2;
            s_node2index[(int)OVRPlugin.Node.TrackerZero] = 3;
            s_node2index[(int)OVRPlugin.Node.TrackerOne] = 4;
            s_node2index[(int)OVRPlugin.Node.TrackerTwo] = 5;
            s_node2index[(int)OVRPlugin.Node.TrackerThree] = 6;

            s_node2class = new VRModuleDeviceClass[(int)OVRPlugin.Node.Count];
            for (int i = 0; i < s_node2class.Length; ++i) { s_node2class[i] = VRModuleDeviceClass.Invalid; }
            s_node2class[(int)OVRPlugin.Node.Head] = VRModuleDeviceClass.HMD;
            s_node2class[(int)OVRPlugin.Node.HandLeft] = VRModuleDeviceClass.Controller;
            s_node2class[(int)OVRPlugin.Node.HandRight] = VRModuleDeviceClass.Controller;
            s_node2class[(int)OVRPlugin.Node.TrackerZero] = VRModuleDeviceClass.TrackingReference;
            s_node2class[(int)OVRPlugin.Node.TrackerOne] = VRModuleDeviceClass.TrackingReference;
            s_node2class[(int)OVRPlugin.Node.TrackerTwo] = VRModuleDeviceClass.TrackingReference;
            s_node2class[(int)OVRPlugin.Node.TrackerThree] = VRModuleDeviceClass.TrackingReference;
        }

        public override bool ShouldActiveModule()
        {
#if UNITY_2019_3_OR_NEWER && VIU_XR_GENERAL_SETTINGS
            return VIUSettings.activateOculusVRModule && (UnityXRModule.HasActiveLoader(OCULUS_XR_LOADER_NAME) ||
                XRSettings.enabled && XRSettings.loadedDeviceName == "Oculus");
#else
            return VIUSettings.activateOculusVRModule && XRSettings.enabled && XRSettings.loadedDeviceName == "Oculus";
#endif
        }

        public override void OnActivated()
        {
            m_prevTrackingSpace = OVRPlugin.GetTrackingOriginType();
            UpdateTrackingSpaceType();

            EnsureDeviceStateLength(VALID_NODE_COUNT);

            LeftHandSkeleton = new Skeleton(OVRPlugin.SkeletonType.HandLeft);
            RightHandSkeleton = new Skeleton(OVRPlugin.SkeletonType.HandRight);

#if VIU_OCULUSVR_1_32_0_OR_NEWER || VIU_OCULUSVR_1_36_0_OR_NEWER || VIU_OCULUSVR_1_37_0_OR_NEWER
            s_moduleInstance = this;
#endif
        }

        public override void OnDeactivated()
        {
            OVRPlugin.SetTrackingOriginType(m_prevTrackingSpace);
        }

        public override void UpdateTrackingSpaceType()
        {
            switch (VRModule.trackingSpaceType)
            {
                case VRModuleTrackingSpaceType.RoomScale:
#if !VIU_OCULUSVR_19_0_OR_NEWER
                    if (OVRPlugin.GetSystemHeadsetType().Equals(OVRPlugin.SystemHeadset.Oculus_Go))
                    {
                        OVRPlugin.SetTrackingOriginType(OVRPlugin.TrackingOrigin.EyeLevel);
                    }
                    else
#endif
                    {
                        OVRPlugin.SetTrackingOriginType(OVRPlugin.TrackingOrigin.FloorLevel);
                    }
                    break;
                case VRModuleTrackingSpaceType.Stationary:
                    OVRPlugin.SetTrackingOriginType(OVRPlugin.TrackingOrigin.EyeLevel);
                    break;
            }
        }

        public override void Update()
        {
            // set physics update rate to vr render rate
            if (VRModule.lockPhysicsUpdateRateToRenderFrequency && Time.timeScale > 0.0f)
            {
                // FIXME: VRDevice.refreshRate returns zero in Unity 5.6.0 or older version
#if !UNITY_5_6_0 && UNITY_5_6_OR_NEWER
                Time.fixedDeltaTime = 1f / XRDevice.refreshRate;
#else
                Time.fixedDeltaTime = 1f / 90f;
#endif
            }
        }

        public override uint GetLeftControllerDeviceIndex()
        {
            return s_node2index[(int)OVRPlugin.Node.HandLeft];
        }

        public override uint GetRightControllerDeviceIndex()
        {
            return s_node2index[(int)OVRPlugin.Node.HandRight];
        }

        private static RigidPose ToPose(OVRPlugin.Posef value)
        {
            var ovrPose = value.ToOVRPose();
            return new RigidPose(ovrPose.position, ovrPose.orientation);
        }

        public override void BeforeRenderUpdate()
        {
            FlushDeviceState();

            for (uint i = 0u, imax = GetDeviceStateLength(); i < imax; ++i)
            {
                var node = s_index2node[i];
                if (node == OVRPlugin.Node.None) { continue; }

                IVRModuleDeviceState prevState;
                IVRModuleDeviceStateRW currState;
                EnsureValidDeviceState(i, out prevState, out currState);

                if (!OVRPlugin.GetNodePresent(node))
                {
                    if (prevState.isConnected)
                    {
                        currState.Reset();
                    }

                    continue;
                }
                
                // update device connected state
                if (!prevState.isConnected)
                {
                    var platform = OVRPlugin.GetSystemHeadsetType();
                    var ovrProductName = platform.ToString();
                    var deviceClass = s_node2class[(int)node];

                    currState.isConnected = true;
                    currState.deviceClass = deviceClass;
                    // FIXME: how to get device id from OVRPlugin?
                    currState.modelNumber = ovrProductName + " " + deviceClass;
                    currState.renderModelName = ovrProductName + " " + deviceClass;
                    currState.serialNumber = ovrProductName + " " + node;

                    switch (deviceClass)
                    {
                        case VRModuleDeviceClass.HMD:
                            currState.deviceModel = VRModuleDeviceModel.OculusHMD;
                            break;
                        case VRModuleDeviceClass.TrackingReference:
                            currState.deviceModel = VRModuleDeviceModel.OculusSensor;
                            break;
                        case VRModuleDeviceClass.Controller:
                            switch (platform)
                            {
#if !VIU_OCULUSVR_19_0_OR_NEWER
                                case OVRPlugin.SystemHeadset.Oculus_Go:
                                    currState.deviceModel = VRModuleDeviceModel.OculusGoController;
                                    currState.input2DType = VRModuleInput2DType.TouchpadOnly;
                                    break;

                                case OVRPlugin.SystemHeadset.GearVR_R320:
                                case OVRPlugin.SystemHeadset.GearVR_R321:
                                case OVRPlugin.SystemHeadset.GearVR_R322:
                                case OVRPlugin.SystemHeadset.GearVR_R323:
                                case OVRPlugin.SystemHeadset.GearVR_R324:
                                case OVRPlugin.SystemHeadset.GearVR_R325:
                                    currState.deviceModel = VRModuleDeviceModel.OculusGearVrController;
                                    currState.input2DType = VRModuleInput2DType.TouchpadOnly;
                                    break;
#endif
                                case OVRPlugin.SystemHeadset.Rift_DK1:
                                case OVRPlugin.SystemHeadset.Rift_DK2:
                                case OVRPlugin.SystemHeadset.Rift_CV1:
                                    switch (node)
                                    {
                                        case OVRPlugin.Node.HandLeft:
                                            currState.deviceModel = VRModuleDeviceModel.OculusTouchLeft;
                                            break;
                                        case OVRPlugin.Node.HandRight:
                                        default:
                                            currState.deviceModel = VRModuleDeviceModel.OculusTouchRight;
                                            break;
                                    }
                                    currState.input2DType = VRModuleInput2DType.JoystickOnly;
                                    break;
#if VIU_OCULUSVR_16_0_OR_NEWER
                                case OVRPlugin.SystemHeadset.Oculus_Link_Quest:
#endif
#if VIU_OCULUSVR_1_37_0_OR_NEWER
                                case OVRPlugin.SystemHeadset.Oculus_Quest:
                                case OVRPlugin.SystemHeadset.Rift_S:
                                    switch (node)
                                    {
                                        case OVRPlugin.Node.HandLeft:
                                            currState.deviceModel = VRModuleDeviceModel.OculusQuestControllerLeft;
                                            break;
                                        case OVRPlugin.Node.HandRight:
                                        default:
                                            currState.deviceModel = VRModuleDeviceModel.OculusQuestControllerRight;
                                            break;
                                    }
                                    currState.input2DType = VRModuleInput2DType.JoystickOnly;
                                    break;
#endif
                            }
                            break;
                    }
                }

                // Hand
                OVRPlugin.HandState handState = new OVRPlugin.HandState();
                if (node == OVRPlugin.Node.HandLeft)
                {
                    OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandLeft, ref handState);
                }
                else if (node == OVRPlugin.Node.HandRight)
                {
                    OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandRight, ref handState);
                }

                if (currState.deviceClass == VRModuleDeviceClass.Controller && (handState.Status & OVRPlugin.HandStatus.HandTracked) != 0)
                {
                    currState.deviceClass = VRModuleDeviceClass.TrackedHand;
                    currState.deviceModel = node == OVRPlugin.Node.HandLeft ? VRModuleDeviceModel.OculusTrackedHandLeft : VRModuleDeviceModel.OculusTrackedHandRight;
                }

                // update device pose
                currState.pose = ToPose(OVRPlugin.GetNodePose(node, OVRPlugin.Step.Render));
                currState.velocity = OVRPlugin.GetNodeVelocity(node, OVRPlugin.Step.Render).FromFlippedZVector3f();
                currState.angularVelocity = OVRPlugin.GetNodeAngularVelocity(node, OVRPlugin.Step.Render).FromFlippedZVector3f();
                currState.isPoseValid = currState.pose != RigidPose.identity;
                currState.isConnected = OVRPlugin.GetNodePresent(node);

                // update device input
                switch (currState.deviceModel)
                {
                    case VRModuleDeviceModel.OculusTouchLeft:
                    case VRModuleDeviceModel.OculusQuestControllerLeft:
                        {
                            var ctrlState = OVRPlugin.GetControllerState((uint)OVRPlugin.Controller.LTouch);

                            currState.SetButtonPress(VRModuleRawButton.ApplicationMenu, (ctrlState.Buttons & (uint)OVRInput.RawButton.Y) != 0u);
                            currState.SetButtonPress(VRModuleRawButton.A, (ctrlState.Buttons & (uint)OVRInput.RawButton.X) != 0u);
                            currState.SetButtonPress(VRModuleRawButton.Touchpad, (ctrlState.Buttons & (uint)OVRInput.RawButton.LThumbstick) != 0u);
                            currState.SetButtonPress(VRModuleRawButton.Trigger, AxisToPress(currState.GetButtonPress(VRModuleRawButton.Trigger), ctrlState.LIndexTrigger, 0.55f, 0.45f));
                            currState.SetButtonPress(VRModuleRawButton.Grip, AxisToPress(currState.GetButtonPress(VRModuleRawButton.Grip), ctrlState.LHandTrigger, 0.55f, 0.45f));
                            currState.SetButtonPress(VRModuleRawButton.CapSenseGrip, AxisToPress(currState.GetButtonPress(VRModuleRawButton.CapSenseGrip), ctrlState.LHandTrigger, 0.55f, 0.45f));
                            currState.SetButtonPress(VRModuleRawButton.System, (ctrlState.Buttons & (uint)OVRInput.RawButton.Start) != 0u);

                            currState.SetButtonTouch(VRModuleRawButton.ApplicationMenu, (ctrlState.Touches & (uint)OVRInput.RawTouch.Y) != 0u);
                            currState.SetButtonTouch(VRModuleRawButton.A, (ctrlState.Touches & (uint)OVRInput.RawTouch.X) != 0u);
                            currState.SetButtonTouch(VRModuleRawButton.Touchpad, (ctrlState.Touches & (uint)OVRInput.RawTouch.LThumbstick) != 0u);
                            currState.SetButtonTouch(VRModuleRawButton.Trigger, (ctrlState.Touches & (uint)OVRInput.RawTouch.LIndexTrigger) != 0u);
                            currState.SetButtonTouch(VRModuleRawButton.Grip, AxisToPress(currState.GetButtonTouch(VRModuleRawButton.Grip), ctrlState.LHandTrigger, 0.25f, 0.20f));
                            currState.SetButtonTouch(VRModuleRawButton.CapSenseGrip, AxisToPress(currState.GetButtonTouch(VRModuleRawButton.CapSenseGrip), ctrlState.LHandTrigger, 0.25f, 0.20f));

                            currState.SetAxisValue(VRModuleRawAxis.TouchpadX, ctrlState.LThumbstick.x);
                            currState.SetAxisValue(VRModuleRawAxis.TouchpadY, ctrlState.LThumbstick.y);
                            currState.SetAxisValue(VRModuleRawAxis.Trigger, ctrlState.LIndexTrigger);
                            currState.SetAxisValue(VRModuleRawAxis.CapSenseGrip, ctrlState.LHandTrigger);
                            break;
                        }
                    case VRModuleDeviceModel.OculusTouchRight:
                    case VRModuleDeviceModel.OculusQuestControllerRight:
                        {
                            var ctrlState = OVRPlugin.GetControllerState((uint)OVRPlugin.Controller.RTouch);

                            currState.SetButtonPress(VRModuleRawButton.ApplicationMenu, (ctrlState.Buttons & (uint)OVRInput.RawButton.B) != 0u);
                            currState.SetButtonPress(VRModuleRawButton.A, (ctrlState.Buttons & (uint)OVRInput.RawButton.A) != 0u);
                            currState.SetButtonPress(VRModuleRawButton.Touchpad, (ctrlState.Buttons & (uint)OVRInput.RawButton.RThumbstick) != 0u);
                            currState.SetButtonPress(VRModuleRawButton.Trigger, AxisToPress(currState.GetButtonPress(VRModuleRawButton.Trigger), ctrlState.RIndexTrigger, 0.55f, 0.45f));
                            currState.SetButtonPress(VRModuleRawButton.Grip, AxisToPress(currState.GetButtonPress(VRModuleRawButton.Grip), ctrlState.RHandTrigger, 0.55f, 0.45f));
                            currState.SetButtonPress(VRModuleRawButton.CapSenseGrip, AxisToPress(currState.GetButtonPress(VRModuleRawButton.CapSenseGrip), ctrlState.RHandTrigger, 0.55f, 0.45f));

                            currState.SetButtonTouch(VRModuleRawButton.ApplicationMenu, (ctrlState.Touches & (uint)OVRInput.RawTouch.B) != 0u);
                            currState.SetButtonTouch(VRModuleRawButton.A, (ctrlState.Touches & (uint)OVRInput.RawTouch.A) != 0u);
                            currState.SetButtonTouch(VRModuleRawButton.Touchpad, (ctrlState.Touches & (uint)OVRInput.RawTouch.RThumbstick) != 0u);
                            currState.SetButtonTouch(VRModuleRawButton.Trigger, (ctrlState.Touches & (uint)OVRInput.RawTouch.RIndexTrigger) != 0u);
                            currState.SetButtonTouch(VRModuleRawButton.Grip, AxisToPress(currState.GetButtonTouch(VRModuleRawButton.Grip), ctrlState.RHandTrigger, 0.25f, 0.20f));
                            currState.SetButtonTouch(VRModuleRawButton.CapSenseGrip, AxisToPress(currState.GetButtonTouch(VRModuleRawButton.CapSenseGrip), ctrlState.RHandTrigger, 0.25f, 0.20f));

                            currState.SetAxisValue(VRModuleRawAxis.TouchpadX, ctrlState.RThumbstick.x);
                            currState.SetAxisValue(VRModuleRawAxis.TouchpadY, ctrlState.RThumbstick.y);
                            currState.SetAxisValue(VRModuleRawAxis.Trigger, ctrlState.RIndexTrigger);
                            currState.SetAxisValue(VRModuleRawAxis.CapSenseGrip, ctrlState.RHandTrigger);
                            break;
                        }
                    case VRModuleDeviceModel.OculusTrackedHandLeft:
                    case VRModuleDeviceModel.OculusTrackedHandRight:
                        if ((handState.Status & OVRPlugin.HandStatus.InputStateValid) != 0)
                        {
                            currState.SetButtonPress(VRModuleRawButton.GestureIndexPinch, (handState.Pinches & OVRPlugin.HandFingerPinch.Index) == OVRPlugin.HandFingerPinch.Index);
                            currState.SetButtonPress(VRModuleRawButton.GestureMiddlePinch, (handState.Pinches & OVRPlugin.HandFingerPinch.Middle) == OVRPlugin.HandFingerPinch.Middle);
                            currState.SetButtonPress(VRModuleRawButton.GestureRingPinch, (handState.Pinches & OVRPlugin.HandFingerPinch.Ring) == OVRPlugin.HandFingerPinch.Ring);
                            currState.SetButtonPress(VRModuleRawButton.GesturePinkyPinch, (handState.Pinches & OVRPlugin.HandFingerPinch.Pinky) == OVRPlugin.HandFingerPinch.Pinky);

                            currState.SetButtonTouch(VRModuleRawButton.GestureIndexPinch, (handState.Pinches & OVRPlugin.HandFingerPinch.Index) == OVRPlugin.HandFingerPinch.Index);
                            currState.SetButtonTouch(VRModuleRawButton.GestureMiddlePinch, (handState.Pinches & OVRPlugin.HandFingerPinch.Middle) == OVRPlugin.HandFingerPinch.Middle);
                            currState.SetButtonTouch(VRModuleRawButton.GestureRingPinch, (handState.Pinches & OVRPlugin.HandFingerPinch.Ring) == OVRPlugin.HandFingerPinch.Ring);
                            currState.SetButtonTouch(VRModuleRawButton.GesturePinkyPinch, (handState.Pinches & OVRPlugin.HandFingerPinch.Pinky) == OVRPlugin.HandFingerPinch.Pinky);

                            currState.SetAxisValue(VRModuleRawAxis.IndexPinch, handState.PinchStrength[(int) HandFinger.Index]);
                            currState.SetAxisValue(VRModuleRawAxis.MiddlePinch, handState.PinchStrength[(int) HandFinger.Middle]);
                            currState.SetAxisValue(VRModuleRawAxis.RingPinch, handState.PinchStrength[(int) HandFinger.Ring]);
                            currState.SetAxisValue(VRModuleRawAxis.PinkyPinch, handState.PinchStrength[(int) HandFinger.Pinky]);
                        }

                        if ((handState.Status & OVRPlugin.HandStatus.HandTracked) != 0)
                        {
                            Skeleton skeleton = node == OVRPlugin.Node.HandLeft ? LeftHandSkeleton : RightHandSkeleton;
                            skeleton.Update(handState);

                            Transform wrist = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_WristRoot];

                            Transform thumbTrapezium = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Thumb0];
                            Transform thumbMetacarpal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Thumb1];
                            Transform thumbProximal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Thumb2];
                            Transform thumbDistal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Thumb3];
                            Transform thumbTip = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_ThumbTip];

                            Transform indexProximal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Index1];
                            Transform indexIntermediate = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Index2];
                            Transform indexDistal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Index3];
                            Transform indexTip = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_IndexTip];

                            Transform middleProximal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Middle1];
                            Transform middleIntermediate = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Middle2];
                            Transform middleDistal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Middle3];
                            Transform middleTip = skeleton.Bones[(int)OVRPlugin.BoneId.Hand_MiddleTip];
                            
                            Transform ringProximal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Ring1];
                            Transform ringIntermediate = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Ring2];
                            Transform ringDistal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Ring3];
                            Transform ringTip = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_RingTip];

                            Transform pinkyMetacarpal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Pinky0];
                            Transform pinkyProximal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Pinky1];
                            Transform pinkyIntermediate = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Pinky2];
                            Transform pinkyDistal = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_Pinky3];
                            Transform pinkyTip = skeleton.Bones[(int) OVRPlugin.BoneId.Hand_PinkyTip];

                            HandJointPose[] joints = currState.handJoints;

                            HandJointPose.AssignToArray(joints, HandJointName.Wrist, wrist.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_WristRoot));

                            HandJointPose.AssignToArray(joints, HandJointName.ThumbTrapezium, thumbTrapezium.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Thumb0));
                            HandJointPose.AssignToArray(joints, HandJointName.ThumbMetacarpal, thumbMetacarpal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Thumb1));
                            HandJointPose.AssignToArray(joints, HandJointName.ThumbProximal, thumbProximal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Thumb2));
                            HandJointPose.AssignToArray(joints, HandJointName.ThumbDistal, thumbDistal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Thumb3));
                            HandJointPose.AssignToArray(joints, HandJointName.ThumbTip, thumbTip.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_ThumbTip));

                            HandJointPose.AssignToArray(joints, HandJointName.IndexProximal, indexProximal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Index1));
                            HandJointPose.AssignToArray(joints, HandJointName.IndexIntermediate, indexIntermediate.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Index2));
                            HandJointPose.AssignToArray(joints, HandJointName.IndexDistal, indexDistal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Index3));
                            HandJointPose.AssignToArray(joints, HandJointName.IndexTip, indexTip.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_IndexTip));

                            HandJointPose.AssignToArray(joints, HandJointName.MiddleProximal, middleProximal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Middle1));
                            HandJointPose.AssignToArray(joints, HandJointName.MiddleIntermediate, middleIntermediate.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Middle2));
                            HandJointPose.AssignToArray(joints, HandJointName.MiddleDistal, middleDistal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Middle3));
                            HandJointPose.AssignToArray(joints, HandJointName.MiddleTip, middleTip.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_MiddleTip));

                            HandJointPose.AssignToArray(joints, HandJointName.RingProximal, ringProximal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Ring1));
                            HandJointPose.AssignToArray(joints, HandJointName.RingIntermediate, ringIntermediate.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Ring2));
                            HandJointPose.AssignToArray(joints, HandJointName.RingDistal, ringDistal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Ring3));
                            HandJointPose.AssignToArray(joints, HandJointName.RingTip, ringTip.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_RingTip));

                            HandJointPose.AssignToArray(joints, HandJointName.PinkyMetacarpal, pinkyMetacarpal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Pinky0));
                            HandJointPose.AssignToArray(joints, HandJointName.PinkyProximal, pinkyProximal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Pinky1));
                            HandJointPose.AssignToArray(joints, HandJointName.PinkyIntermediate, pinkyIntermediate.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Pinky2));
                            HandJointPose.AssignToArray(joints, HandJointName.PinkyDistal, pinkyDistal.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_Pinky3));
                            HandJointPose.AssignToArray(joints, HandJointName.PinkyTip, pinkyTip.position, skeleton.GetOpenXRRotation(OVRPlugin.BoneId.Hand_PinkyTip));
                        }

                        break;
#if !VIU_OCULUSVR_19_0_OR_NEWER
                    case VRModuleDeviceModel.OculusGoController:
                    case VRModuleDeviceModel.OculusGearVrController:
                        switch (node)
                        {
                            case OVRPlugin.Node.HandLeft:
                                {
                                    var ctrlState = OVRPlugin.GetControllerState4((uint)OVRPlugin.Controller.LTrackedRemote);

                                    currState.SetButtonPress(VRModuleRawButton.Touchpad, (ctrlState.Buttons & (uint)OVRInput.RawButton.LTouchpad) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.ApplicationMenu, (ctrlState.Buttons & (uint)OVRInput.RawButton.Back) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.Trigger, (ctrlState.Buttons & (uint)(OVRInput.RawButton.A | OVRInput.RawButton.LIndexTrigger)) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.DPadLeft, (ctrlState.Buttons & (uint)OVRInput.RawButton.DpadLeft) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.DPadUp, (ctrlState.Buttons & (uint)OVRInput.RawButton.DpadUp) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.DPadRight, (ctrlState.Buttons & (uint)OVRInput.RawButton.DpadRight) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.DPadDown, (ctrlState.Buttons & (uint)OVRInput.RawButton.DpadDown) != 0u);

                                    currState.SetButtonTouch(VRModuleRawButton.Touchpad, (ctrlState.Touches & (uint)OVRInput.RawTouch.LTouchpad) != 0u);

                                    currState.SetAxisValue(VRModuleRawAxis.TouchpadX, ctrlState.LTouchpad.x);
                                    currState.SetAxisValue(VRModuleRawAxis.TouchpadY, ctrlState.LTouchpad.y);
                                }
                                break;
                            case OVRPlugin.Node.HandRight:
                            default:
                                {
                                    var ctrlState = OVRPlugin.GetControllerState4((uint)OVRPlugin.Controller.RTrackedRemote);

                                    currState.SetButtonPress(VRModuleRawButton.Touchpad, (ctrlState.Buttons & unchecked((uint)OVRInput.RawButton.RTouchpad)) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.ApplicationMenu, (ctrlState.Buttons & (uint)OVRInput.RawButton.Back) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.Trigger, (ctrlState.Buttons & (uint)(OVRInput.RawButton.A | OVRInput.RawButton.RIndexTrigger)) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.DPadLeft, (ctrlState.Buttons & (uint)OVRInput.RawButton.DpadLeft) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.DPadUp, (ctrlState.Buttons & (uint)OVRInput.RawButton.DpadUp) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.DPadRight, (ctrlState.Buttons & (uint)OVRInput.RawButton.DpadRight) != 0u);
                                    currState.SetButtonPress(VRModuleRawButton.DPadDown, (ctrlState.Buttons & (uint)OVRInput.RawButton.DpadDown) != 0u);

                                    currState.SetButtonTouch(VRModuleRawButton.Touchpad, (ctrlState.Touches & unchecked((uint)OVRInput.RawTouch.RTouchpad)) != 0u);

                                    currState.SetAxisValue(VRModuleRawAxis.TouchpadX, ctrlState.RTouchpad.x);
                                    currState.SetAxisValue(VRModuleRawAxis.TouchpadY, ctrlState.RTouchpad.y);
                                }
                                break;
                        }
                        break;
#endif
                }
            }

            ProcessConnectedDeviceChanged();
            ProcessDevicePoseChanged();
            ProcessDeviceInputChanged();
        }
#endif
    }
}
