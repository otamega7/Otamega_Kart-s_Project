using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
// 新しいInput Systemを使用
using UnityEngine.InputSystem;
// Unity 6 (Cinemachine 3.x) 対応
using Unity.Cinemachine;

public class KartController : MonoBehaviour
{
    private Volume postVolume;
    private ChromaticAberration chromaticAberration;

    public Transform kartModel;
    public Transform kartNormal;
    public Rigidbody sphere;

    public List<ParticleSystem> primaryParticles = new List<ParticleSystem>();
    public List<ParticleSystem> secondaryParticles = new List<ParticleSystem>();

    float speed, currentSpeed;
    float rotate, currentRotate;
    int driftDirection;
    float driftPower;
    int driftMode = 0;
    bool first, second, third;
    Color c;

    [Header("Bools")]
    public bool drifting;

    [Header("Parameters")]
    public float acceleration = 30f;
    public float steering = 80f;
    public float gravity = 10f;
    public LayerMask layerMask;

    [Header("Model Parts")]
    public Transform frontWheels;
    public Transform backWheels;
    public Transform steeringWheel;

    [Header("Particles")]
    public Transform wheelParticles;
    public Transform flashParticles;
    public Color[] turboColors;

    // --- Input System用の定義 ---
    private InputAction steerAction;
    private InputAction accelerateAction;
    private InputAction driftAction;

    private void Awake()
    {
        // コード内で入力を定義（設定ファイル不要で即動作するように構成）

        // 1. ハンドル操作 (A/Dキー または 左スティック)
        steerAction = new InputAction("Steer", binding: "<Gamepad>/leftStick/x");
        steerAction.AddCompositeBinding("Axis")
            .With("Negative", "<Keyboard>/a")
            .With("Positive", "<Keyboard>/d")
            .With("Negative", "<Keyboard>/leftArrow")
            .With("Positive", "<Keyboard>/rightArrow");

        // 2. 加速 (左クリック、Ctrl、ゲームパッドの南ボタン/R2)
        accelerateAction = new InputAction("Accelerate", binding: "<Gamepad>/buttonSouth"); // Aボタン/×ボタン
        accelerateAction.AddBinding("<Keyboard>/leftCtrl");
        accelerateAction.AddBinding("<Mouse>/leftButton");
        accelerateAction.AddBinding("<Gamepad>/rightTrigger");

        // 3. ドリフト (スペースキー、ゲームパッドの東ボタン/R1)
        driftAction = new InputAction("Drift", binding: "<Gamepad>/buttonEast"); // Bボタン/○ボタン
        driftAction.AddBinding("<Keyboard>/space");
        driftAction.AddBinding("<Gamepad>/rightShoulder");
    }

    private void OnEnable()
    {
        steerAction.Enable();
        accelerateAction.Enable();
        driftAction.Enable();
    }

    private void OnDisable()
    {
        steerAction.Disable();
        accelerateAction.Disable();
        driftAction.Disable();
    }
    // ---------------------------

    void Start()
    {
        postVolume = Camera.main.GetComponent<Volume>();

        if (postVolume != null && postVolume.profile.TryGet(out ChromaticAberration ca))
        {
            chromaticAberration = ca;
        }

        if (wheelParticles != null)
        {
            for (int i = 0; i < wheelParticles.GetChild(0).childCount; i++)
            {
                primaryParticles.Add(wheelParticles.GetChild(0).GetChild(i).GetComponent<ParticleSystem>());
            }

            for (int i = 0; i < wheelParticles.GetChild(1).childCount; i++)
            {
                primaryParticles.Add(wheelParticles.GetChild(1).GetChild(i).GetComponent<ParticleSystem>());
            }
        }

        if (flashParticles != null)
        {
            foreach (ParticleSystem p in flashParticles.GetComponentsInChildren<ParticleSystem>())
            {
                secondaryParticles.Add(p);
            }
        }
    }

    void Update()
    {
        // 入力値の取得（新しいInput System経由）
        float steerInput = steerAction.ReadValue<float>();
        bool accelerateInput = accelerateAction.IsPressed();
        bool driftInputDown = driftAction.WasPressedThisFrame();
        bool driftInputUp = driftAction.WasReleasedThisFrame();


        // Sphereの位置に追従
        transform.position = sphere.transform.position - new Vector3(0, 0.4f, 0);

        // 加速
        if (accelerateInput)
            speed = acceleration;

        // ステアリング
        if (steerInput != 0)
        {
            int dir = steerInput > 0 ? 1 : -1;
            float amount = Mathf.Abs(steerInput);
            Steer(dir, amount);
        }

        // ドリフト開始
        if (driftInputDown && !drifting && steerInput != 0)
        {
            drifting = true;
            driftDirection = steerInput > 0 ? 1 : -1;

            foreach (ParticleSystem p in primaryParticles)
            {
                var main = p.main;
                main.startColor = Color.clear;
                p.Play();
            }

            kartModel.parent.DOComplete();
            kartModel.parent.DOPunchPosition(transform.up * .2f, .3f, 5, 1);
        }

        // ドリフト中
        if (drifting)
        {
            // ExtensionMethods.Remap を使用して入力値を調整
            float control = (driftDirection == 1) ?
                ExtensionMethods.Remap(steerInput, -1, 1, 0, 2) :
                ExtensionMethods.Remap(steerInput, -1, 1, 2, 0);

            float powerControl = (driftDirection == 1) ?
                ExtensionMethods.Remap(steerInput, -1, 1, .2f, 1) :
                ExtensionMethods.Remap(steerInput, -1, 1, 1, .2f);

            Steer(driftDirection, control);
            driftPower += powerControl;

            ColorDrift();
        }

        // ドリフト解除 & ブースト
        if (driftInputUp && drifting)
        {
            Boost();
        }

        currentSpeed = Mathf.SmoothStep(currentSpeed, speed, Time.deltaTime * 12f); speed = 0f;
        currentRotate = Mathf.Lerp(currentRotate, rotate, Time.deltaTime * 4f); rotate = 0f;

        // アニメーション
        // a) Kart本体
        if (!drifting)
        {
            // 90 を削除して、0 (または削除) にします
            kartModel.localEulerAngles = Vector3.Lerp(kartModel.localEulerAngles, new Vector3(0, (steerInput * 15), kartModel.localEulerAngles.z), .2f);
        }
        else
        {
            float control = (driftDirection == 1) ? ExtensionMethods.Remap(steerInput, -1, 1, .5f, 2) : ExtensionMethods.Remap(steerInput, -1, 1, 2, .5f);
            kartModel.parent.localRotation = Quaternion.Euler(0, Mathf.LerpAngle(kartModel.parent.localEulerAngles.y, (control * 15) * driftDirection, .2f), 0);
        }

        // b) タイヤ
        if (frontWheels != null && backWheels != null)
        {
            frontWheels.localEulerAngles = new Vector3(0, (steerInput * 15), frontWheels.localEulerAngles.z);
            frontWheels.localEulerAngles += new Vector3(0, 0, sphere.linearVelocity.magnitude / 2);
            backWheels.localEulerAngles += new Vector3(0, 0, sphere.linearVelocity.magnitude / 2);
        }

        // c) ハンドル
        if (steeringWheel != null)
            steeringWheel.localEulerAngles = new Vector3(-25, 90, ((steerInput * 45)));

    }

    private void FixedUpdate()
    {
        // 物理挙動
        if (!drifting)
            sphere.AddForce(kartModel.transform.forward * currentSpeed, ForceMode.Acceleration);
        else
            sphere.AddForce(transform.forward * currentSpeed, ForceMode.Acceleration);

        sphere.AddForce(Vector3.down * gravity, ForceMode.Acceleration);

        transform.eulerAngles = Vector3.Lerp(transform.eulerAngles, new Vector3(0, transform.eulerAngles.y + currentRotate, 0), Time.deltaTime * 5f);

        RaycastHit hitOn;
        RaycastHit hitNear;

        Physics.Raycast(transform.position + (transform.up * .1f), Vector3.down, out hitOn, 1.1f, layerMask);
        Physics.Raycast(transform.position + (transform.up * .1f), Vector3.down, out hitNear, 2.0f, layerMask);

        kartNormal.up = Vector3.Lerp(kartNormal.up, hitNear.normal, Time.deltaTime * 8.0f);
        kartNormal.Rotate(0, transform.eulerAngles.y, 0);
    }

    public void Boost()
    {
        drifting = false;

        if (driftMode > 0)
        {
            DOVirtual.Float(currentSpeed * 3, currentSpeed, .3f * driftMode, Speed);
            DOVirtual.Float(0, 1, .5f, ChromaticAmount).OnComplete(() => DOVirtual.Float(1, 0, .5f, ChromaticAmount));

            var tube1 = kartModel.Find("Tube001");
            var tube2 = kartModel.Find("Tube002");
            if (tube1) tube1.GetComponentInChildren<ParticleSystem>().Play();
            if (tube2) tube2.GetComponentInChildren<ParticleSystem>().Play();
        }

        driftPower = 0;
        driftMode = 0;
        first = false; second = false; third = false;

        foreach (ParticleSystem p in primaryParticles)
        {
            var main = p.main;
            main.startColor = Color.clear;
            p.Stop();
        }

        kartModel.parent.DOLocalRotate(Vector3.zero, .5f).SetEase(Ease.OutBack);
    }

    public void Steer(int direction, float amount)
    {
        rotate = (steering * direction) * amount;
    }

    public void ColorDrift()
    {
        if (!first) c = Color.clear;

        if (driftPower > 50 && driftPower < 100 - 1 && !first)
        {
            first = true;
            c = turboColors[0];
            driftMode = 1;
            PlayFlashParticle(c);
        }

        if (driftPower > 100 && driftPower < 150 - 1 && !second)
        {
            second = true;
            c = turboColors[1];
            driftMode = 2;
            PlayFlashParticle(c);
        }

        if (driftPower > 150 && !third)
        {
            third = true;
            c = turboColors[2];
            driftMode = 3;
            PlayFlashParticle(c);
        }

        foreach (ParticleSystem p in primaryParticles)
        {
            var pmain = p.main;
            pmain.startColor = c;
        }

        foreach (ParticleSystem p in secondaryParticles)
        {
            var pmain = p.main;
            pmain.startColor = c;
        }
    }

    void PlayFlashParticle(Color c)
    {
        GameObject cam = GameObject.Find("CM vcam1");
        if (cam != null)
        {
            // Cinemachine 3.x 対応の呼び出し
            var impulse = cam.GetComponent<CinemachineImpulseSource>();
            if (impulse != null) impulse.GenerateImpulse();
        }

        foreach (ParticleSystem p in secondaryParticles)
        {
            var pmain = p.main;
            pmain.startColor = c;
            p.Play();
        }
    }

    private void Speed(float x)
    {
        currentSpeed = x;
    }

    void ChromaticAmount(float x)
    {
        if (chromaticAberration != null)
        {
            chromaticAberration.intensity.value = x;
        }
    }
}