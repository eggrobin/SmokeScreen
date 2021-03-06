﻿/*
 * Copyright (c) 2014, Sébastien GAGGINI AKA Sarbian, France
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 * 
 * 1. Redistributions of source code must retain the above copyright notice, this
 *    list of conditions and the following disclaimer.
 * 
 * 2. Redistributions in binary form must reproduce the above copyright notice,
 *    this list of conditions and the following disclaimer in the documentation
 *    and/or other materials provided with the distribution.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
 * FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
 * DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
 * OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 * 
 */

using System;
using System.Collections.Generic;
using SmokeScreen;
using UnityEngine;

[EffectDefinition("MODEL_MULTI_PARTICLE_PERSIST")]
public class ModelMultiParticlePersistFX : EffectBehaviour
{
    #region Persistent fields

    [Persistent]
    public string modelName = string.Empty;

    [Persistent]
    public string transformName = string.Empty;

    [Persistent]
    public string shaderFileName = string.Empty;

    [Persistent]
    public string renderMode = "Billboard";

    [Persistent]
    public bool collide = false;

    [Persistent]
    public float collideRatio = 0.0f;

    [Persistent]
    public Vector3 localRotation = Vector3.zero;

    [Persistent]
    public Vector3 localPosition = Vector3.zero;

    [Persistent]
    public Vector3 offsetDirection = Vector3.forward;

    [Persistent]
    public float fixedScale = 1;

    [Persistent]
    public float sizeClamp = 50;

    // Initial density of the particle seen as sphere of radius size of perfect 
    // gas. We then assume (only true for ideally expanded exhaust) that the 
    // expansion is isobaric (by mixing with the atmosphere) in order to compute
    // the density afterwards. Units (SI): kg / m^3.
    [Persistent]
    public double initialDensity = .6;

    // Whether to apply Archimedes' force, gravity and other things to the 
    // particle.
    [Persistent]
    public bool physical = false;

    // How much the particles stick to objects they collide with.
    [Persistent]
    public double stickiness = 0.9;

    [Persistent]
    public double dragCoefficient = 0.1;

    // Current Time % timeModulo is used as the time input
    [Persistent]
    public float timeModulo = 10;

    // For how long the effect will be running after a single Emit()
    // time input is overridden to be the remaining time while it runs
    [Persistent]
    public float singleEmitTimer = 0;

    // The initial velocity of the particles will be offset by a random amount
    // lying in a disk perpendicular to the mean initial velocity whose radius
    // is randomOffsetMaxRadius. This is similar to Unity's 'Random Velocity'
    // Setting, except it will sample the offset from a (normal) disk rather
    // than from a cube. Units (SI): m/s.
    // TODO Sarbian : have the init auto fill this one
    [Persistent]
    public float randomInitalVelocityOffsetMaxRadius = 0.0f;

    #endregion Persistent fields

    public MultiInputCurve emission;

    public MultiInputCurve energy;

    public MultiInputCurve speed;

    public MultiInputCurve grow;

    public MultiInputCurve scale;

    public MultiInputCurve size;

    public MultiInputCurve offset;

    public MultiInputCurve force;

    // Logarithmic growth applied to to the particle.
    // The size at time t after emission will be approximately
    // (Log(logarithmicGrowth * t + 1) + 1) * initialSize, assuming grow = 0.
    public MultiInputCurve logGrow;
    
    // Those 2 curve are related to the angle and distance to cam
    public FXCurve angle = new FXCurve("angle", 1f);

    public FXCurve distance = new FXCurve("distance", 1f);

    private List<PersistentKSPParticleEmitter> persistentEmitters;

    private Shader shader;

    public string node_backup = string.Empty;

    private bool activated = true;

    public bool showUI = false;

    private static readonly List<ModelMultiParticlePersistFX> list = new List<ModelMultiParticlePersistFX>();

    private float singleTimerEnd = 0;
    private float timeModuloDelta = 0;

    public static List<ModelMultiParticlePersistFX> List
    {
        get
        {
            return list;
        }
    }

    public bool overRideInputs = false;

    public readonly float[] inputs = new float[MultiInputCurve.inputsCount];

    public List<ModelMultiParticlePersistFX> Instances
    {
        get
        {
            return list;
        }
    }

    public ModelMultiParticlePersistFX()
    {
        winID = baseWinID++;
    }

    //~ModelMultiParticlePersistFX()
    //{
    //    print("DESTROY ALL HUMAN");
    //    list.Remove(this);
    //}

    private void OnDestroy()
    {
        if (persistentEmitters != null)
        {
            for (int i = 0; i < persistentEmitters.Count; i++)
            {
                persistentEmitters[i].Detach(0);
            }
        }
        list.Remove(this);
    }

    public override void OnEvent()
    {
        if (!activated || persistentEmitters == null)
        {
            return;
        }
        singleTimerEnd = this.singleEmitTimer + Time.fixedTime;
        timeModuloDelta = singleTimerEnd % timeModulo;
        UpdateEmitters(1);
        for (int i = 0; i < persistentEmitters.Count; i++)
        {
            persistentEmitters[i].pe.Emit();
        }
    }

    public override void OnEvent(float power)
    {
        if (persistentEmitters == null)
        {
            return;
        }


        //if (power > 0f && activated)
        if (activated)
        {
            UpdateEmitters(power);
            for (int i = 0; i < persistentEmitters.Count; i++)
            {
                persistentEmitters[i].fixedEmit = true;
                persistentEmitters[i].pe.emit = false;
            }
        }
        else
        {
            for (int j = 0; j < persistentEmitters.Count; j++)
            {
                persistentEmitters[j].fixedEmit = false;
                persistentEmitters[j].pe.emit = false;
            }
        }
    }

    public void FixedUpdate()
    {
        if (persistentEmitters == null)
        {
            return;
        }

        SmokeScreenConfig.UpdateParticlesCount();

        

        //RaycastHit vHit = new RaycastHit();
        //Ray vRay = Camera.main.ScreenPointToRay(Input.mousePosition);
        //if(Physics.Raycast(vRay, out vHit))
        //{
        //    RaycastHit vHit2 = new RaycastHit();
        //    if (Physics.Raycast(vHit.point + vHit.normal * 10, -vHit.normal, out vHit2))
        //        Debug.Log(vHit2.collider.name);
        //}
    

        PersistentKSPParticleEmitter[] persistentKspParticleEmitters = persistentEmitters.ToArray();
        for (int i = 0; i < persistentKspParticleEmitters.Length; i++)
        {
            PersistentKSPParticleEmitter persistentKspParticleEmitter = persistentKspParticleEmitters[i];

            persistentKspParticleEmitter.EmitterOnUpdate(this.hostPart.rb.velocity + Krakensbane.GetFrameVelocity());
        }
    }
   
    
    private void UpdateInputs(float power)
    {
        if (overRideInputs)
        {
            return;
        }

        float atmDensity = 1;
        float surfaceVelMach = 1;
        float partTemp = 1;
        float externalTemp = 1;
        // timeModuloDelta makes the transition between the two state smooth 
        float time = Time.deltaTime >= singleTimerEnd
                         ? (Time.deltaTime - timeModuloDelta) % timeModulo
                         : singleTimerEnd - Time.deltaTime;

        if (hostPart != null)
        {
            partTemp = hostPart.temperature;

            if (hostPart.vessel != null)
            {
                Vessel vessel = hostPart.vessel;
                atmDensity = (float)vessel.atmDensity;

                externalTemp = vessel.flightIntegrator.getExternalTemperature();

                // FAR use a nice config file to get the atmo info for each body. 
                // For now I'll just use Air for all.
                const double magicNumberFromFAR = 1.4 * 8.3145 * 1000 / 28.96;
                double speedOfSound = Math.Sqrt((externalTemp + 273.15) * magicNumberFromFAR);
                surfaceVelMach = (float)(vessel.srf_velocity.magnitude / speedOfSound);
            }
            else
            {
                atmDensity =
                    (float)
                    FlightGlobals.getAtmDensity(
                        FlightGlobals.getStaticPressure(hostPart.transform.position, FlightGlobals.currentMainBody));
                externalTemp = FlightGlobals.getExternalTemperature(hostPart.transform.position);
                const double magicNumberFromFAR = 1.4 * 8.3145 * 1000 / 28.96;
                double speedOfSound = Math.Sqrt((externalTemp + 273.15) * magicNumberFromFAR);
                surfaceVelMach =
                    (float)
                    ((hostPart.vel - FlightGlobals.currentMainBody.getRFrmVel(hostPart.transform.position)).magnitude
                     / speedOfSound);
            }
        }

        inputs[(int)MultiInputCurve.Inputs.power] = power;
        inputs[(int)MultiInputCurve.Inputs.density] = atmDensity;
        inputs[(int)MultiInputCurve.Inputs.mach] = surfaceVelMach;
        inputs[(int)MultiInputCurve.Inputs.parttemp] = partTemp;
        inputs[(int)MultiInputCurve.Inputs.externaltemp] = externalTemp;
        inputs[(int)MultiInputCurve.Inputs.time] = time;

    }

    public void UpdateEmitters(float power)
    {
        UpdateInputs(power);

        for (int i = 0; i < persistentEmitters.Count; i++)
        {
            PersistentKSPParticleEmitter pkpe = persistentEmitters[i];

            float sizePower = size.Value(inputs) * fixedScale;
            pkpe.pe.minSize = Mathf.Min(pkpe.minSizeBase * sizePower, sizeClamp);
            pkpe.pe.maxSize = Mathf.Min(pkpe.maxSizeBase * sizePower, sizeClamp);

            float emissionPower = emission.Value(inputs);
            pkpe.pe.minEmission = Mathf.FloorToInt(pkpe.minEmissionBase * emissionPower);
            pkpe.pe.maxEmission = Mathf.FloorToInt(pkpe.maxEmissionBase * emissionPower);

            float energyPower = energy.Value(inputs);
            pkpe.pe.minEnergy = pkpe.minEnergyBase * energyPower;
            pkpe.pe.maxEnergy = pkpe.maxEnergyBase * energyPower;

            float velocityPower = speed.Value(inputs);
            pkpe.pe.localVelocity = pkpe.localVelocityBase * velocityPower;
            pkpe.pe.worldVelocity = pkpe.worldVelocityBase * velocityPower;

            float forcePower = force.Value(inputs);
            pkpe.pe.force = pkpe.forceBase * forcePower;

            pkpe.pe.sizeGrow = grow.Value(inputs);

            float currentScale = scale.Value(inputs) * fixedScale;
            pkpe.pe.shape1D = pkpe.scale1DBase * currentScale;
            pkpe.pe.shape2D = pkpe.scale2DBase * currentScale;
            pkpe.pe.shape3D = pkpe.scale3DBase * currentScale;


            pkpe.sizeClamp = sizeClamp;
            pkpe.randomInitalVelocityOffsetMaxRadius = randomInitalVelocityOffsetMaxRadius;

            pkpe.physical = physical && !SmokeScreenConfig.Instance.globalPhysicalDisable;
            pkpe.initialDensity = initialDensity;
            pkpe.dragCoefficient = dragCoefficient;

            pkpe.collide = collide && !SmokeScreenConfig.Instance.globalCollideDisable;
            pkpe.stickiness = stickiness;
            pkpe.collideRatio = collideRatio;

            pkpe.logarithmicGrow = logGrow.Value(inputs);

            pkpe.go.transform.localPosition = localPosition
                                              + offsetDirection.normalized * offset.Value(inputs) * fixedScale;

            pkpe.go.transform.localRotation = Quaternion.Euler(localRotation);

            // Bad code is bad
            try
            {
                pkpe.pe.particleRenderMode =
                    (ParticleRenderMode)Enum.Parse(typeof(ParticleRenderMode), renderMode);
            }
            catch (ArgumentException)
            {
            }

            ////print(atmDensity.ToString("F2") + " " + offset.Value(power).ToString("F2") + " " + offsetFromDensity.Value(atmDensity).ToString("F2") + " " + offsetFromMach.Value(surfaceVelMach).ToString("F2"));
        }
    }

    public void Update()
    {
        if (persistentEmitters == null)
        {
            return;
        }
        for (int i = 0; i < persistentEmitters.Count; i++)
        {
            // using Camera.main will mess up anything multi cam but using current require adding a OnWillRenderObject() to the ksp particle emitter GameObject (? not tested)
            float currentAngle = Vector3.Angle(
                -Camera.main.transform.forward,
                persistentEmitters[i].go.transform.forward);
            float currentDist = (Camera.main.transform.position - persistentEmitters[i].go.transform.position).magnitude;

            persistentEmitters[i].pe.maxParticleSize = persistentEmitters[i].maxSizeBase * angle.Value(currentAngle)
                                                       * distance.Value(currentDist);
            persistentEmitters[i].pe.pr.maxParticleSize = persistentEmitters[i].pe.maxParticleSize;
        }
    }

    public override void OnInitialize()
    {
        // Restore the Curve config from the node content backup
        // Done because I could not get the serialization of MultiInputCurve to work
        if (node_backup != string.Empty)
        {
            Restore();
        }

        // The shader loading require proper testing
        // Unity doc says that "Creating materials this way supports only simple shaders (fixed function ones). 
        // If you need a surface shader, or vertex/pixel shaders, you'll need to create shader asset in the editor and use that."
        // But importing the same shader that the one used in the editor seems to work
        string filename = KSPUtil.ApplicationRootPath + "GameData/" + shaderFileName;
        if (shaderFileName != string.Empty && System.IO.File.Exists(filename))
        {
            try
            {
                System.IO.TextReader shaderFile = new System.IO.StreamReader(filename);
                string shaderText = shaderFile.ReadToEnd();
                shader = new Material(shaderText).shader;
            }
            catch (Exception e)
            {
                print("unable to load shader " + shaderFileName + " : " + e.ToString());
            }
        }

        List<Transform> transforms = new List<Transform>(hostPart.FindModelTransforms(transformName));
        if (transforms.Count == 0)
        {
            print("Cannot find transform " + transformName);
            return;
        }
        GameObject model = GameDatabase.Instance.GetModel(modelName);
        if (model == null)
        {
            print("Cannot find model " + modelName);
            return;
        }
        model.SetActive(true);
        KSPParticleEmitter templateKspParticleEmitter = model.GetComponentInChildren<KSPParticleEmitter>();

        if (templateKspParticleEmitter == null)
        {
            print("Cannot find particle emitter on " + modelName);
            UnityEngine.Object.Destroy(model);
            return;
        }

        if (shader != null)
        {
            templateKspParticleEmitter.material.shader = shader;
        }

        if (persistentEmitters == null)
        {
            persistentEmitters = new List<PersistentKSPParticleEmitter>();
        }

        for (int i = 0; i < transforms.Count; i++)
        {
            GameObject emitterGameObject = UnityEngine.Object.Instantiate(model) as GameObject;
            KSPParticleEmitter childKSPParticleEmitter = emitterGameObject.GetComponentInChildren<KSPParticleEmitter>();

            if (childKSPParticleEmitter != null)
            {
                PersistentKSPParticleEmitter pkpe = new PersistentKSPParticleEmitter(
                    emitterGameObject,
                    childKSPParticleEmitter,
                    templateKspParticleEmitter);

                try
                {
                    childKSPParticleEmitter.particleRenderMode =
                        (ParticleRenderMode)Enum.Parse(typeof(ParticleRenderMode), renderMode);
                }
                catch (ArgumentException)
                {
                    print("ModelMultiParticleFXExt: " + renderMode + " is not a valid ParticleRenderMode");
                }

                persistentEmitters.Add(pkpe);

                emitterGameObject.transform.SetParent(transforms[i]);

                emitterGameObject.transform.localPosition = localPosition;
                emitterGameObject.transform.localRotation = Quaternion.Euler(localRotation);
            }
        }

        UnityEngine.Object.Destroy(templateKspParticleEmitter);

        list.Add(this);
    }

    public void Backup(ConfigNode node)
    {
        node_backup = SmokeScreenUtil.WriteRootNode(node);
        //print("Backup node_backup is\n " + node_backup.Replace(Environment.NewLine, Environment.NewLine + "ModelMultiParticlePersistFX "));
    }

    public void Restore()
    {
        //print("Restore node_backup is\n " + node_backup.Replace(Environment.NewLine, Environment.NewLine + "ModelMultiParticlePersistFX "));
        string[] text = node_backup.Split(new string[] { "\n" }, StringSplitOptions.None);
        ConfigNode node = SmokeScreenUtil.RecurseFormat(SmokeScreenUtil.PreFormatConfig(text));
        this.OnLoad(node);
    }

    public override void OnLoad(ConfigNode node)
    {
        //print("OnLoad");
        Backup(node);

        emission = new MultiInputCurve("emission");
        energy = new MultiInputCurve("energy");
        speed = new MultiInputCurve("speed");
        grow = new MultiInputCurve("grow", true);
        scale = new MultiInputCurve("scale");
        size = new MultiInputCurve("size");
        offset = new MultiInputCurve("offset", true);
        force = new MultiInputCurve("force", true);
        logGrow = new MultiInputCurve("logGrow", true);

        ConfigNode.LoadObjectFromConfig(this, node);
        emission.Load(node);
        energy.Load(node);
        speed.Load(node);
        grow.Load(node);
        scale.Load(node);
        size.Load(node);
        offset.Load(node);
        force.Load(node);

        logGrow.Load(node);

        angle.Load("angle", node);
        distance.Load("distance", node);
    }

    public override void OnSave(ConfigNode node)
    {
        ConfigNode.CreateConfigFromObject(this, node);
        emission.Save(node);
        energy.Save(node);
        speed.Save(node);
        grow.Save(node);
        scale.Save(node);
        size.Save(node);
        offset.Save(node);
        force.Save(node);
        logGrow.Save(node);

        angle.Save(node);
        distance.Save(node);
    }

    private static void print(String s)
    {
        MonoBehaviour.print("[ModelMultiParticlePersistFX] " + s);
    }

    // TODO : move the whole UI stuff to a dedicated class - this is getting way to big

    private Rect winPos = new Rect(50, 50, 400, 100);

    private static int baseWinID = 512100;

    private static int winID = baseWinID;

    private string nodeText = "";

    private bool nodeEdit = false;

    private Vector2 scrollPosition = new Vector2();

    private void OnGUI()
    {
        if (!HighLogic.LoadedSceneIsFlight)
        {
            return;
        }
        if (showUI && hostPart != null)
        {
            winPos = GUILayout.Window(
                winID,
                winPos,
                windowGUI,
                hostPart.name + " " + this.effectName + " " + this.instanceName,
                GUILayout.MinWidth(300));
        }
    }

    private void windowGUI(int ID)
    {
        GUILayout.BeginVertical();

        activated = GUILayout.Toggle(activated, "Active");

        GUILayout.Space(10);

        overRideInputs = GUILayout.Toggle(overRideInputs, "Manual Inputs");

        GUIInput((int)MultiInputCurve.Inputs.power, "Power");
        GUIInput((int)MultiInputCurve.Inputs.density, "Atmo Density");
        GUIInput((int)MultiInputCurve.Inputs.mach, "Mach Speed");
        GUIInput((int)MultiInputCurve.Inputs.parttemp, "Part Temperature");
        GUIInput((int)MultiInputCurve.Inputs.externaltemp, "External Temperature");

        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Single Emit Timer"))
        {
            this.OnEvent();
        }

        if (GUILayout.Button("Clear Particles"))
        {
            for (int i = 0; i < persistentEmitters.Count; i++)
            {
                persistentEmitters[i].pe.pe.ClearParticles();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        nodeEdit = GUILayout.Toggle(nodeEdit, "Open Config Editor");

        if (nodeEdit)
        {
            GUILayout.BeginHorizontal();

            // Set the node with what was in the .cfg
            if (GUILayout.Button("Import"))
            {
                nodeText = string.Copy(node_backup);
                //print("Displaying node \n " + nodeText.Replace("\n", "\n" + "ModelMultiParticlePersistFX "));
            }

            // Rebuild the text from the active config
            if (GUILayout.Button("Rebuild"))
            {
                ConfigNode node = new ConfigNode();
                this.OnSave(node);
                nodeText = SmokeScreenUtil.WriteRootNode(node);
            }

            // Apply the text
            if (GUILayout.Button("Apply"))
            {
                string[] text = nodeText.Split(new string[] { "\n" }, StringSplitOptions.None);
                ConfigNode node = SmokeScreenUtil.RecurseFormat(SmokeScreenUtil.PreFormatConfig(text));
                this.OnLoad(node);
            }

            GUILayout.EndHorizontal();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.MinHeight(300));

            nodeText = GUILayout.TextArea(nodeText, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();
        }

        GUILayout.EndVertical();

        GUI.DragWindow();
    }

    private readonly bool[] boxInput = new bool[MultiInputCurve.inputsCount];

    private void GUIInput(int id, string text)
    {
        float min = minInput(id);
        float max = maxInput(id);

        GUILayout.Label(
            text + " Val=" + inputs[id].ToString("F3") + " Min=" + min.ToString("F2") + " Max=" + max.ToString("F2"));

        if (overRideInputs)
        {
            GUILayout.BeginHorizontal();
            boxInput[id] = GUILayout.Toggle(boxInput[id], "", GUILayout.ExpandWidth(false));

            if (boxInput[id])
            {
                float.TryParse(
                    GUILayout.TextField(inputs[id].ToString("F2"), GUILayout.ExpandWidth(true), GUILayout.Width(100)),
                    out inputs[id]);
            }
            else
            {
                inputs[id] = GUILayout.HorizontalSlider(
                    inputs[id],
                    minInput(id),
                    maxInput(id),
                    GUILayout.ExpandWidth(true));
            }

            GUILayout.EndHorizontal();
        }
    }

    private float minInput(int id)
    {
        float min = emission.minInput[id];
        min = Mathf.Min(min, energy.minInput[id]);
        min = Mathf.Min(min, speed.minInput[id]);
        min = Mathf.Min(min, grow.minInput[id]);
        min = Mathf.Min(min, scale.minInput[id]);
        min = Mathf.Min(min, size.minInput[id]);
        min = Mathf.Min(min, offset.minInput[id]);
        min = Mathf.Min(min, force.minInput[id]);
        min = Mathf.Min(min, logGrow.minInput[id]);

        return min;
    }
    
    private float maxInput(int id)
    {
        float max = emission.maxInput[id];
        max = Mathf.Max(max, energy.maxInput[id]);
        max = Mathf.Max(max, speed.maxInput[id]);
        max = Mathf.Max(max, grow.maxInput[id]);
        max = Mathf.Max(max, scale.maxInput[id]);
        max = Mathf.Max(max, size.maxInput[id]);
        max = Mathf.Max(max, offset.maxInput[id]);
        max = Mathf.Max(max, force.maxInput[id]);
        max = Mathf.Max(max, logGrow.maxInput[id]);

        return max;
    }
}