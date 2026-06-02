using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

public struct CapturyInputState : IInputStateTypeInfo
{
    public FourCC format => new FourCC('C', 'A', 'P', 'T');
    // PLAYER IDENTIFICATION
    [InputControl(layout = "Integer")]
    public int playerIndex;


    // TORSO TRACKING CONTROLS
    [InputControl(layout = "Button")]
    public float isBentOver;

    [InputControl(layout = "Button")]
    public float isUpright;

    [InputControl(layout = "Button")]
    public float weightShiftLeft;

    [InputControl(layout = "Button")]
    public float weightShiftRight;

    [InputControl(layout = "Axis")]
    public float weightShiftX;

    [InputControl(layout = "Vector3")]
    public Vector3 pelvisPosition;

    // FOOT TRACKING CONTROLS
    [InputControl(layout = "Button")]
    public float footRaised;

    [InputControl(layout = "Button")]
    public float footLowered;

    [InputControl(layout = "Vector3")]
    public Vector3 leftFootPosition;

    [InputControl(layout = "Vector3")]
    public Vector3 rightFootPosition;

    [InputControl(layout = "Button")]
    public float leftHipAbducted;

    [InputControl(layout = "Button")]
    public float rightHipAbducted;

    // WALK TRACKING CONTROLS
    [InputControl(layout = "Button")]
    public float isWalking;

    [InputControl(layout = "Button")]
    public float walkStarted;

    [InputControl(layout = "Button")]
    public float walkStopped;

    [InputControl(layout = "Axis")]
    public float walkSpeed;

    [InputControl(layout = "Axis")]
    public float cadence; // steps per minute

    // GAIT ANALYSIS CONTROLS
    [InputControl(layout = "Button")]
    public float leftStep;

    [InputControl(layout = "Button")]
    public float rightStep;

    [InputControl(layout = "Axis")]
    public float leftStepTime;

    [InputControl(layout = "Axis")]
    public float rightStepTime;

    [InputControl(layout = "Axis")]
    public float stepTimeAsymmetry;

    [InputControl(layout = "Axis")]
    public float gaitConsistency;

    // ARM/HAND TRACKING CONTROLS
    [InputControl(layout = "Vector3")]
    public Vector3 leftHandPosition;

    [InputControl(layout = "Vector3")]
    public Vector3 rightHandPosition;

    [InputControl(layout = "Button")]
    public float leftHandRaised;

    [InputControl(layout = "Button")]
    public float rightHandRaised;

    // HEAD TRACKING CONTROLS
    [InputControl(layout = "Vector3")]
    public Vector3 headPosition;

    [InputControl(layout = "Vector3")]
    public Vector3 headRotation;

    [InputControl(layout = "Button")]
    public float headUp;

    [InputControl(layout = "Button")]
    public float headDown;

    [InputControl(layout = "Button")]
    public float headLeft;

    [InputControl(layout = "Button")]
    public float headRight;
    // BALANCE TRACKING CONTROLS
    [InputControl(layout = "Vector3")]
    public Vector3 centerOfMassPosition;

    [InputControl(layout = "Axis")]
    public float lateralSway;

    [InputControl(layout = "Axis")]
    public float anteriorPosteriorSway;

    [InputControl(layout = "Axis")]
    public float swayMagnitude;

    [InputControl(layout = "Button")]
    public float isSwaying;

    [InputControl(layout = "Axis")]
    public float comVelocity;

    [InputControl(layout = "Button")]
    public float isBalanced;

    [InputControl(layout = "Button")]
    public float balanceLost;

    [InputControl(layout = "Button")]
    public float balanceRegained;
}

[InputControlLayout(stateType = typeof(CapturyInputState), displayName = "Captury Input")]
public class CapturyInput : InputDevice
{
    [InputControl(layout = "Integer", displayName = "Player Index")]
    public IntegerControl playerIndex { get; private set; }
    // TORSO CONTROLS
    [InputControl(layout = "Button", displayName = "Is Bent Over")]
    public ButtonControl isBentOver { get; private set; }

    [InputControl(layout = "Button", displayName = "Is Upright")]
    public ButtonControl isUpright { get; private set; }

    [InputControl(layout = "Button", displayName = "Weight Shift Left")]
    public ButtonControl weightShiftLeft { get; private set; }

    [InputControl(layout = "Button", displayName = "Weight Shift Right")]
    public ButtonControl weightShiftRight { get; private set; }

    [InputControl(layout = "Axis", displayName = "Weight Shift X-Axis")]
    public AxisControl weightShiftX { get; private set; }

    [InputControl(layout = "Vector3", displayName = "Pelvis Position")]
    public Vector3Control pelvisPosition { get; private set; }

    // FOOT CONTROLS
    [InputControl(layout = "Button", displayName = "Foot Raised")]
    public ButtonControl footRaised { get; private set; }

    [InputControl(layout = "Button", displayName = "Foot Lowered")]
    public ButtonControl footLowered { get; private set; }

    [InputControl(layout = "Vector3", displayName = "Left Foot Position")]
    public Vector3Control leftFootPosition { get; private set; }

    [InputControl(layout = "Vector3", displayName = "Right Foot Position")]
    public Vector3Control rightFootPosition { get; private set; }

    [InputControl(layout = "Button", displayName = "Left Hip Abducted")]
    public ButtonControl leftHipAbducted { get; private set; }

    [InputControl(layout = "Button", displayName = "Right Hip Abducted")]
    public ButtonControl rightHipAbducted { get; private set; }

    // WALK TRACKING CONTROLS
    [InputControl(layout = "Button", displayName = "Is Walking")]
    public ButtonControl isWalking { get; private set; }

    [InputControl(layout = "Button", displayName = "Walk Started")]
    public ButtonControl walkStarted { get; private set; }

    [InputControl(layout = "Button", displayName = "Walk Stopped")]
    public ButtonControl walkStopped { get; private set; }

    [InputControl(layout = "Axis", displayName = "Walk Speed")]
    public AxisControl walkSpeed { get; private set; }

    [InputControl(layout = "Axis", displayName = "Cadence")]
    public AxisControl cadence { get; private set; }

    // GAIT ANALYSIS
    [InputControl(layout = "Button", displayName = "Left Step")]
    public ButtonControl leftStep { get; private set; }

    [InputControl(layout = "Button", displayName = "Right Step")]
    public ButtonControl rightStep { get; private set; }

    [InputControl(layout = "Axis", displayName = "Left Step Time")]
    public AxisControl leftStepTime { get; private set; }

    [InputControl(layout = "Axis", displayName = "Right Step Time")]
    public AxisControl rightStepTime { get; private set; }

    [InputControl(layout = "Axis", displayName = "Step Time Asymmetry")]
    public AxisControl stepTimeAsymmetry { get; private set; }

    [InputControl(layout = "Axis", displayName = "Gait Consistency")]
    public AxisControl gaitConsistency { get; private set; }

    // ARM/HAND CONTROLS
    [InputControl(layout = "Vector3", displayName = "Left Hand Position")]
    public Vector3Control leftHandPosition { get; private set; }

    [InputControl(layout = "Vector3", displayName = "Right Hand Position")]
    public Vector3Control rightHandPosition { get; private set; }

    [InputControl(layout = "Button", displayName = "Left Hand Raised")]
    public ButtonControl leftHandRaised { get; private set; }

    [InputControl(layout = "Button", displayName = "Right Hand Raised")]
    public ButtonControl rightHandRaised { get; private set; }

    // HEAD TRACKING CONTROLS
    [InputControl(layout = "Vector3", displayName = "Head Position")]
    public Vector3Control headPosition { get; private set; }

    [InputControl(layout = "Vector3", displayName = "Head Rotation")]
    public Vector3Control headRotation { get; private set; }

    [InputControl(layout = "Button", displayName = "Head Up")]
    public ButtonControl headUp { get; private set; }

    [InputControl(layout = "Button", displayName = "Head Down")]
    public ButtonControl headDown { get; private set; }

    [InputControl(layout = "Button", displayName = "Head Left")]
    public ButtonControl headLeft { get; private set; }

    [InputControl(layout = "Button", displayName = "Head Right")]
    public ButtonControl headRight { get; private set; }
    // BALANCE TRACKING CONTROLS
    [InputControl(layout = "Vector3", displayName = "Center of Mass Position")]
    public Vector3Control centerOfMassPosition { get; private set; }

    [InputControl(layout = "Axis", displayName = "Lateral Sway")]
    public AxisControl lateralSway { get; private set; }

    [InputControl(layout = "Axis", displayName = "Anterior-Posterior Sway")]
    public AxisControl anteriorPosteriorSway { get; private set; }

    [InputControl(layout = "Axis", displayName = "Sway Magnitude")]
    public AxisControl swayMagnitude { get; private set; }

    [InputControl(layout = "Button", displayName = "Is Swaying")]
    public ButtonControl isSwaying { get; private set; }

    [InputControl(layout = "Axis", displayName = "CoM Velocity")]
    public AxisControl comVelocity { get; private set; }

    [InputControl(layout = "Button", displayName = "Is Balanced")]
    public ButtonControl isBalanced { get; private set; }

    [InputControl(layout = "Button", displayName = "Balance Lost")]
    public ButtonControl balanceLost { get; private set; }

    [InputControl(layout = "Button", displayName = "Balance Regained")]
    public ButtonControl balanceRegained { get; private set; }

    protected override void FinishSetup()
    {
        base.FinishSetup();

        playerIndex = GetChildControl<IntegerControl>("playerIndex");

        // torso controls
        isBentOver = GetChildControl<ButtonControl>("isBentOver");
        isUpright = GetChildControl<ButtonControl>("isUpright");
        weightShiftLeft = GetChildControl<ButtonControl>("weightShiftLeft");
        weightShiftRight = GetChildControl<ButtonControl>("weightShiftRight");
        weightShiftX = GetChildControl<AxisControl>("weightShiftX");
        pelvisPosition = GetChildControl<Vector3Control>("pelvisPosition");

        // foot controls
        footRaised = GetChildControl<ButtonControl>("footRaised");
        footLowered = GetChildControl<ButtonControl>("footLowered");
        leftFootPosition = GetChildControl<Vector3Control>("leftFootPosition");
        rightFootPosition = GetChildControl<Vector3Control>("rightFootPosition");
        leftHipAbducted = GetChildControl<ButtonControl>("leftHipAbducted");
        rightHipAbducted = GetChildControl<ButtonControl>("rightHipAbducted");

        // walk tracking controls
        isWalking = GetChildControl<ButtonControl>("isWalking");
        walkStarted = GetChildControl<ButtonControl>("walkStarted");
        walkStopped = GetChildControl<ButtonControl>("walkStopped");
        walkSpeed = GetChildControl<AxisControl>("walkSpeed");
        cadence = GetChildControl<AxisControl>("cadence");

        // gait analysis controls
        leftStep = GetChildControl<ButtonControl>("leftStep");
        rightStep = GetChildControl<ButtonControl>("rightStep");
        leftStepTime = GetChildControl<AxisControl>("leftStepTime");
        rightStepTime = GetChildControl<AxisControl>("rightStepTime");
        stepTimeAsymmetry = GetChildControl<AxisControl>("stepTimeAsymmetry");
        gaitConsistency = GetChildControl<AxisControl>("gaitConsistency");

        // arm/hand controls
        leftHandPosition = GetChildControl<Vector3Control>("leftHandPosition");
        rightHandPosition = GetChildControl<Vector3Control>("rightHandPosition");
        leftHandRaised = GetChildControl<ButtonControl>("leftHandRaised");
        rightHandRaised = GetChildControl<ButtonControl>("rightHandRaised");

        // head controls
        headPosition = GetChildControl<Vector3Control>("headPosition");
        headRotation = GetChildControl<Vector3Control>("headRotation");
        headUp = GetChildControl<ButtonControl>("headUp");
        headDown = GetChildControl<ButtonControl>("headDown");
        headLeft = GetChildControl<ButtonControl>("headLeft");
        headRight = GetChildControl<ButtonControl>("headRight");

        // balance controls
        centerOfMassPosition = GetChildControl<Vector3Control>("centerOfMassPosition");
        lateralSway = GetChildControl<AxisControl>("lateralSway");
        anteriorPosteriorSway = GetChildControl<AxisControl>("anteriorPosteriorSway");
        swayMagnitude = GetChildControl<AxisControl>("swayMagnitude");
        isSwaying = GetChildControl<ButtonControl>("isSwaying");
        comVelocity = GetChildControl<AxisControl>("comVelocity");
        isBalanced = GetChildControl<ButtonControl>("isBalanced");
        balanceLost = GetChildControl<ButtonControl>("balanceLost");
        balanceRegained = GetChildControl<ButtonControl>("balanceRegained");

        Debug.Log("CapturyInput setup complete - Torso, Foot, Walk, Gait, Arm/Hand, Head, and Balance tracking controls ready");
    }

    public static void Register()
    {
        InputSystem.RegisterLayout<CapturyInput>(
            matches: new InputDeviceMatcher().WithInterface("Custom"),
            name: "CapturyInput");

        Debug.Log("CapturyInput layout registered with Input System");
    }
}