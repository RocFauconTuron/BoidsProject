using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine.Jobs;
using Unity.Collections;

[BurstCompile]
public class BoidsManagerJobs : MonoBehaviour
{
    public BoidSettings boidSettings;
    List<Transform> boids;
    public GameObject prefab;
    public float spawnRadius = 10;
    public int spawnCount = 10;
    bool doneSpawning = false;
    float timer = 0;
    NativeArray<BoidConstantData> boidConstantData;
    BoidSettingsData boidSettingsData;

    NativeArray<uint> seeds;
    NativeArray<int> changedData;

    UpdateBoidsJob updateBoids;
    CheckBoidsForJob checkBoidsJob;

    void Awake()
    {
        boids = new List<Transform>();
        boidConstantData = new NativeArray<BoidConstantData>(spawnCount, Allocator.Persistent);
        seeds = new NativeArray<uint>(spawnCount, Allocator.Persistent);
        changedData = new NativeArray<int>(spawnCount, Allocator.Persistent);

        float startSpeed = (boidSettings.minSpeed + boidSettings.maxSpeed) / 2;

        for (int i = 0; i < spawnCount; i++)
        {
            float3 pos = transform.position + UnityEngine.Random.insideUnitSphere * spawnRadius;
            GameObject boid = Instantiate(prefab,(Vector3)pos,quaternion.identity);
            float3 forward = UnityEngine.Random.insideUnitSphere;
            boid.transform.forward = (math.normalize(forward));
            boids.Add(boid.transform);

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
        updateBoids = new UpdateBoidsJob()
        {
            boidConstantData = boidConstantData,
            boidSettingsData = boidSettingsData
        };
    }
    private void OnDisable()
    {
        boidConstantData.Dispose();
        seeds.Dispose();
        changedData.Dispose();
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
            boids[i].position = boidConstantData[i].position;
            boids[i].forward = boidConstantData[i].forward;

            BoidCurrentData data = boidData[i];
            BoidConstantData currentBoidConstantData = boidConstantData[i];

            data.position = boids[i].position;
            data.direction = boids[i].forward;
            data.globalDir = currentBoidConstantData.globalDirConstant;
            boidData[i] = data;

            if (canSteer)
            {
                currentBoidConstantData.globalDirConstant = float3.zero;
            }

            seeds[i] = (uint)UnityEngine.Random.Range(1, 100000);

            if (IsHeadingForCollision(boidConstantData[i]))
            {
                currentBoidConstantData.collisionAvoidDir = ObstacleRays(boids[i], currentBoidConstantData);
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
        checkBoidsJob.Schedule(spawnCount, 64).Complete();

        updateBoids.deltaTime = Time.deltaTime;
        updateBoids.Schedule(spawnCount, 64).Complete();

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

    float3 ObstacleRays(Transform boidTransform, BoidConstantData currentBoid)
    {
        Vector3[] rayDirections = BoidHelper.directions;

        for (int i = 0; i < rayDirections.Length; i++)
        {
            Vector3 dir = boidTransform.TransformDirection(rayDirections[i]);
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
public struct CheckBoidsJob : IJob
{
    [ReadOnly] public bool canSteer;
    [ReadOnly] public int numBoids;
    [ReadOnly] public float wanderJitter;
    [ReadOnly] public float perceptionRadius;
    [ReadOnly] public float avoidanceRadius;
    [ReadOnly] uint seed;

    public NativeArray<BoidCurrentData> boidData;
    public NativeArray<int> changedData;
    public NativeArray<BoidConstantData> boidConstantData;

    public void Execute()
    {
        for (int i = 0; i < numBoids; i++)
        {
            // Crea un generador de números aleatorios con un estado único por índice
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed + (uint)i);

            bool hasChangedOtherGlobalDir = false;
            BoidCurrentData currentBoid = boidData[i];
            int changedBoids = 0;

            if (canSteer)
            {
                float groupDirJitter = wanderJitter;
                currentBoid.globalDir = new float3(random.NextFloat(-groupDirJitter, groupDirJitter), random.NextFloat(-groupDirJitter, groupDirJitter), random.NextFloat(-groupDirJitter, groupDirJitter));
            }

            for (int j = 0; j < numBoids; j++)
            {
                if (i != j)
                {
                    BoidCurrentData otherBoid = boidData[j];
                    float3 awayFromNeighbor = otherBoid.position - currentBoid.position;
                    float distanceFromNeightBor = math.length(awayFromNeighbor);

                    if (distanceFromNeightBor < perceptionRadius * perceptionRadius)
                    {
                        currentBoid.numNeightbors += 1;
                        currentBoid.flockHeading += otherBoid.direction;
                        currentBoid.flockCentre += otherBoid.position;
                        if (canSteer)
                        {
                            if ((otherBoid.globalDir.x != 0) && (otherBoid.globalDir.y != 0) && (otherBoid.globalDir.z != 0))
                            {
                                currentBoid.globalDir = otherBoid.globalDir;
                                if (hasChangedOtherGlobalDir)
                                {
                                    for (int z = 0; z < changedData.Length - 1; z++)
                                    {
                                        if (changedData[z] == 0) break;

                                        BoidCurrentData boidChanged = boidData[changedData[z]];
                                        boidChanged.globalDir = currentBoid.globalDir;
                                        boidData[changedData[z]] = boidChanged;
                                        changedData[z] = 0;
                                        changedBoids = 0;
                                    }
                                }
                            }
                            else
                            {
                                hasChangedOtherGlobalDir = true;
                                otherBoid.globalDir = currentBoid.globalDir;
                                boidData[j] = otherBoid;
                                changedData[changedBoids] = j;
                                changedBoids++;
                            }
                        }

                        if (distanceFromNeightBor < avoidanceRadius * avoidanceRadius)
                        {
                            currentBoid.flockSeparation -= awayFromNeighbor / distanceFromNeightBor;
                        }
                    }
                }
            }
            boidData[i] = currentBoid;

            BoidConstantData currentBoidConstantData = boidConstantData[i];
            currentBoidConstantData.avgFlockDirection = boidData[i].flockHeading;
            currentBoidConstantData.centreOfFlockmates = boidData[i].flockCentre;
            currentBoidConstantData.avgSeparationDirection = boidData[i].flockSeparation;
            currentBoidConstantData.numPerceivedFlockmates = boidData[i].numNeightbors;
            if (canSteer)
            {
                currentBoidConstantData.globalDirConstant = boidData[i].globalDir;
            }
            boidConstantData[i] = currentBoidConstantData;
        }
 
    }
}

[BurstCompile]
struct UpdateBoidsJob : IJobParallelFor
{
    public NativeArray<BoidConstantData> boidConstantData;
    [ReadOnly] public BoidSettingsData boidSettingsData;
    [ReadOnly] public float deltaTime;

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

        boidConstantData[index] = currentBoid;
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

public struct BoidCurrentData
{
    public float3 position;
    public float3 direction;
    public float3 globalDir;

    public float3 flockHeading;
    public float3 flockCentre;
    public float3 flockSeparation;
    public int numNeightbors;
}
public struct BoidConstantData
{
    public float3 avgFlockDirection;
    public float3 avgSeparationDirection;
    public float3 centreOfFlockmates;
    public int numPerceivedFlockmates;
    public float3 globalDirConstant;
    public float3 position;
    public float3 forward;
    public float3 collisionAvoidDir;
    public bool hasFoundCollision;
    public float3 velocity;
    public quaternion rotation;
}
public struct BoidSettingsData
{
    // Settings
    public float minSpeed;
    public float maxSpeed;
    public float maxSteerForce;

    public float alignWeight;
    public float cohesionWeight;
    public float seperateWeight;

    public float maxSteeringForce;
    public float steerSpeed;
    public float avoidCollisionWeight;
}
