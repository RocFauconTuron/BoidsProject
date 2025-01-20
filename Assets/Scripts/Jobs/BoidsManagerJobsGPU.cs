using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine.Jobs;
using Unity.Collections;

[BurstCompile]
public class BoidsManagerJobsGPU : MonoBehaviour
{
    public BoidSettings boidSettings;
    public float spawnRadius = 10;
    public int spawnCount = 10;
    bool doneSpawning = false;
    float timer = 0;
    NativeArray<BoidConstantData> boidConstantData;
    BoidSettingsData boidSettingsData;

    NativeArray<int> changedData;

    UpdateBoidsJobGPU updateBoids;
    NativeArray<Matrix4x4> _nativeMatrices;

    RenderParams _rp;
    public Mesh mesh;
    public Material mat;

    public Vector3 scale;
    public Vector3 angle;

    CheckBoidsForJob checkBoidsJob;

    void Awake()
    {
        boidConstantData = new NativeArray<BoidConstantData>(spawnCount, Allocator.Persistent);
        changedData = new NativeArray<int>(spawnCount, Allocator.Persistent);
        _nativeMatrices = new NativeArray<Matrix4x4>(spawnCount, Allocator.Persistent);

        float startSpeed = (boidSettings.minSpeed + boidSettings.maxSpeed) / 2;

        for (int i = 0; i < spawnCount; i++)
        {
            float3 pos = transform.position + UnityEngine.Random.insideUnitSphere * spawnRadius;
            float3 forward = UnityEngine.Random.insideUnitSphere;

            BoidConstantData currentBoidConstantData = boidConstantData[i];

            currentBoidConstantData.position = pos;
            currentBoidConstantData.forward = forward;
            currentBoidConstantData.velocity = forward * startSpeed;

            boidConstantData[i] = currentBoidConstantData;
        }
        timer = 100000;
        doneSpawning = true;

        boidSettingsData = new BoidSettingsData
        {
            minSpeed = boidSettings.minSpeed,
            maxSpeed = boidSettings.maxSpeed,
            maxSteerForce = boidSettings.maxSteerForce,
            alignWeight = boidSettings.alignWeight,
            cohesionWeight = boidSettings.cohesionWeight,
            seperateWeight = boidSettings.seperateWeight,
            maxSteeringForce = boidSettings.maxSteeringForce,
            steerSpeed = boidSettings.steerSpeed,
            avoidCollisionWeight = boidSettings.avoidCollisionWeight
        };
        checkBoidsJob = new CheckBoidsForJob()
        {
            numBoids = spawnCount,
            wanderJitter = boidSettings.wanderJitter,
            perceptionRadius = boidSettings.perceptionRadius,
            avoidanceRadius = boidSettings.avoidanceRadius,
            boidConstantData = boidConstantData
        };

        updateBoids = new UpdateBoidsJobGPU
        {
            boidConstantData = boidConstantData,
            boidSettingsData = boidSettingsData,
            Matrices = _nativeMatrices,
            scale = (float3)scale,
            angle = (float3)angle
        };

        _rp = new RenderParams(mat);
    }
    private void OnDisable()
    {
        boidConstantData.Dispose();
        changedData.Dispose();
        _nativeMatrices.Dispose();
    }
    void Update()
    {
        if (!doneSpawning) return;

        NativeArray<BoidCurrentData> boidData = new NativeArray<BoidCurrentData>(spawnCount, Allocator.TempJob);
        bool canSteer = false;

        if (timer > UnityEngine.Random.Range(boidSettings.timeMinWander, boidSettings.timeMaxWander))
        {
            canSteer = true;
            timer = 0;
        }
        timer += Time.deltaTime;

        for (int i = 0; i < spawnCount; i++)
        {
            BoidCurrentData data = boidData[i];
            BoidConstantData currentBoidConstantData = boidConstantData[i];

            data.position = currentBoidConstantData.position;
            data.direction = currentBoidConstantData.forward;
            data.globalDir = currentBoidConstantData.globalDirConstant;

            boidData[i] = data;
           
            if (canSteer)
            {
                currentBoidConstantData.globalDirConstant = float3.zero;
            }

            if (IsHeadingForCollision(boidConstantData[i]))
            {
                currentBoidConstantData.collisionAvoidDir = ObstacleRays(currentBoidConstantData);
                currentBoidConstantData.hasFoundCollision = true;
            }
            else
            {
                currentBoidConstantData.hasFoundCollision = false;
            }

            boidConstantData[i] = currentBoidConstantData;
        }

        uint seed = (uint)UnityEngine.Random.Range(1, 100000);

        checkBoidsJob.boidData = boidData;
        checkBoidsJob.canSteer = canSteer;
        checkBoidsJob.seed = seed;
        checkBoidsJob.Schedule(spawnCount,64).Complete();


        updateBoids.deltaTime = Time.deltaTime;
        updateBoids.Schedule(spawnCount, 64).Complete();


        Graphics.RenderMeshInstanced(_rp, mesh, 0, _nativeMatrices);
        boidData.Dispose();
    }
    bool IsHeadingForCollision(BoidConstantData currentBoid)
    {
        RaycastHit hit;
        if (Physics.SphereCast(currentBoid.position, boidSettings.boundsRadius, currentBoid.forward, out hit, boidSettings.collisionAvoidDst, boidSettings.obstacleMask))
        {
            return true;
        }
        else { }
        return false;
    }

    float3 ObstacleRays(BoidConstantData currentBoid)
    {
        Vector3[] rayDirections = BoidHelper.directions;

        Vector3 angle = math.Euler(currentBoid.rotation);
        for (int i = 0; i < rayDirections.Length; i++)
        {
            Vector3 dir = (Quaternion)currentBoid.rotation * rayDirections[i];
            Ray ray = new Ray(currentBoid.position, dir);
            if (!Physics.SphereCast(ray, boidSettings.boundsRadius, boidSettings.collisionAvoidDst, boidSettings.obstacleMask))
            {
                return dir;
            }
        }

        return currentBoid.forward;
    }
}
[BurstCompile]
public struct CheckBoidsForJob : IJobParallelFor
{
    [ReadOnly] public bool canSteer;
    [ReadOnly] public int numBoids;
    [ReadOnly] public float wanderJitter;
    [ReadOnly] public float perceptionRadius;
    [ReadOnly] public float avoidanceRadius;
    [ReadOnly] public uint seed;

    [ReadOnly]public NativeArray<BoidCurrentData> boidData; // Solo lectura para evitar conflictos
    public NativeArray<BoidConstantData> boidConstantData;   // Escritura permitida en índice propio

    public void Execute(int index)
    {
        // Crea un generador de números aleatorios con un estado único por índice
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed + (uint)index);

        bool changedDirByBoid = false;
        BoidCurrentData currentBoid = boidData[index];
        BoidConstantData currentBoidConstantData = boidConstantData[index];
        int numPercivedFlocks = 0;
        float3 avgDir = float3.zero;
        float3 centerflocks = float3.zero;
        float3 avgSeparationDir = float3.zero;

        for (int j = 0; j < numBoids; j++)
        {
            if (index != j)
            {
                BoidCurrentData otherBoid = boidData[j];
                float3 awayFromNeighbor = otherBoid.position - currentBoid.position;
                float distanceFromNeightBor = math.length(awayFromNeighbor);

                if (distanceFromNeightBor < perceptionRadius * perceptionRadius)
                {
                    numPercivedFlocks += 1;
                    avgDir += otherBoid.direction;
                    centerflocks += otherBoid.position;
                    if (canSteer)
                    {
                        if ((otherBoid.globalDir.x != 0) && (otherBoid.globalDir.y != 0) && (otherBoid.globalDir.z != 0))
                        {
                            currentBoidConstantData.globalDirConstant = otherBoid.globalDir;
                            changedDirByBoid = true;
                        }
                    }

                    if (distanceFromNeightBor < avoidanceRadius * avoidanceRadius)
                    {
                        avgSeparationDir -= awayFromNeighbor / distanceFromNeightBor;
                    }
                }
            }
        }

        if (canSteer)
        {
            if (!changedDirByBoid)
            {
                float groupDirJitter = wanderJitter;
                currentBoidConstantData.globalDirConstant = new float3(random.NextFloat(-groupDirJitter, groupDirJitter), random.NextFloat(-groupDirJitter, groupDirJitter), random.NextFloat(-groupDirJitter, groupDirJitter));
            }
        }
        currentBoidConstantData.numPerceivedFlockmates = numPercivedFlocks;
        currentBoidConstantData.avgFlockDirection = avgDir;
        currentBoidConstantData.centreOfFlockmates = centerflocks;
        currentBoidConstantData.avgSeparationDirection = avgSeparationDir;

        boidConstantData[index] = currentBoidConstantData;
    }
}

[BurstCompile]
struct UpdateBoidsJobGPU : IJobParallelFor
{
    public NativeArray<BoidConstantData> boidConstantData;
    [ReadOnly] public BoidSettingsData boidSettingsData;
    [ReadOnly] public float deltaTime;
    [ReadOnly] public float3 scale;
    [ReadOnly] public float3 angle;
    [WriteOnly] public NativeArray<Matrix4x4> Matrices;

    public void Execute(int index)
    {
        float3 acceleration = float3.zero;
        BoidConstantData currentBoid = boidConstantData[index];

        if (currentBoid.numPerceivedFlockmates != 0)
        {
            currentBoid.centreOfFlockmates /= currentBoid.numPerceivedFlockmates;

            float3 offsetToFlockmatesCentre = (currentBoid.centreOfFlockmates - currentBoid.position);
            float3 alignmentForce = SteerTowards(currentBoid.avgFlockDirection, boidSettingsData.maxSpeed, currentBoid) * boidSettingsData.alignWeight;
            float3 cohesionForce = SteerTowards(offsetToFlockmatesCentre, boidSettingsData.maxSpeed, currentBoid) * boidSettingsData.cohesionWeight;

            float3 seperationForce = float3.zero;
            if (currentBoid.avgSeparationDirection.x != 0 && currentBoid.avgSeparationDirection.y != 0 && currentBoid.avgSeparationDirection.z != 0)
            {
                seperationForce = SteerTowards(currentBoid.avgSeparationDirection, boidSettingsData.maxSpeed, currentBoid) * boidSettingsData.seperateWeight;
            }           
            acceleration += alignmentForce;
            acceleration += cohesionForce;
            acceleration += seperationForce;
        }
        float3 globalDirForce = SteerTowards(currentBoid.globalDirConstant, boidSettingsData.steerSpeed, currentBoid) * boidSettingsData.maxSteeringForce;
        acceleration += globalDirForce;

        if (currentBoid.hasFoundCollision)
        {
            float3 collisionAvoidForce = SteerTowards(currentBoid.collisionAvoidDir, boidSettingsData.maxSpeed, currentBoid) * boidSettingsData.avoidCollisionWeight;
            acceleration += collisionAvoidForce;
        }

        currentBoid.velocity += acceleration * deltaTime;
        float speed = math.length(currentBoid.velocity);
        Vector3 dir = currentBoid.velocity / speed;
        speed = math.clamp(speed, boidSettingsData.minSpeed, boidSettingsData.maxSpeed);

        currentBoid.velocity = dir * speed;
        currentBoid.position = currentBoid.position + currentBoid.velocity * deltaTime;
        currentBoid.forward = dir;

        quaternion rotation = quaternion.LookRotation(math.normalize(dir), math.up());
        quaternion additionalRotation = quaternion.EulerXYZ(math.radians(angle));
        currentBoid.rotation = rotation;

        boidConstantData[index] = currentBoid;

        Matrices[index] = Matrix4x4.TRS(currentBoid.position, math.mul(rotation, additionalRotation), scale);
    }
    float3 SteerTowards(float3 vector, float speed, BoidConstantData currentBoid)
    {
        float3 v = math.normalize(vector) * speed - currentBoid.velocity;
        //return math.clamp(v, float3.zero, new float3(boidSettingsData.maxSteerForce, boidSettingsData.maxSteerForce, boidSettingsData.maxSteerForce));
        return ClampMagnitude(v, boidSettingsData.maxSteerForce);
    }
    private float3 ClampMagnitude(float3 v,  float maxMagnitude)
    {
        float magnitude = math.length(v); // Magnitud del vector

        if (magnitude > maxMagnitude)
        {
            return v / magnitude * maxMagnitude; // Normaliza y escala al máximo permitido
        }

        return v; // Devuelve el vector sin cambios si está dentro del rango
    }
}
