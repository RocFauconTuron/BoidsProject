using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine.Jobs;
using Unity.Collections;
using System;

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

    public float3 scale;
    public float3 angle;
    public float3 bounds;
    public float cellSize = 2f;

    NativeArray<HashAndIndex> hashAndIndices;

    void Awake()
    {
        boidConstantData = new NativeArray<BoidConstantData>(spawnCount, Allocator.Persistent);
        changedData = new NativeArray<int>(spawnCount, Allocator.Persistent);
        _nativeMatrices = new NativeArray<Matrix4x4>(spawnCount, Allocator.Persistent);
        hashAndIndices = new NativeArray<HashAndIndex>(spawnCount, Allocator.Persistent);

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
            wanderWeight = boidSettings.wanderWeight,
            steerSpeed = boidSettings.steerSpeed,
            avoidCollisionWeight = boidSettings.avoidCollisionWeight
        };

        float goldenRatio = (1 + Mathf.Sqrt(5)) / 2;
        float angleIncrement = Mathf.PI * 2 * goldenRatio;

        _rp = new RenderParams(mat);
    }
    private void OnDestroy()
    {
        boidConstantData.Dispose();
        changedData.Dispose();
        _nativeMatrices.Dispose();
        hashAndIndices.Dispose();
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



        GetBoidInHashJob hashJob = new GetBoidInHashJob
        {
            boids = boidConstantData,
            cellSize = cellSize,
            hashAndIndices = hashAndIndices
        };      
        JobHandle hashJobHandle = hashJob.Schedule(spawnCount, 64);

        SortHashCodesJob sortJob = new SortHashCodesJob
        {
            hashAndIndices = hashAndIndices
        };
        JobHandle sortJobHandle = sortJob.Schedule(hashJobHandle);

        var queryJob = new QueryJob
        {
            boidData = boidData,
            hashAndIndices = hashAndIndices,
            cellSize = cellSize,
            canSteer = canSteer,
            wanderJitter = boidSettings.wanderJitter,
            perceptionRadius = boidSettings.perceptionRadius,
            avoidanceRadius = boidSettings.avoidanceRadius,
            seed = seed,
            boidConstantData = boidConstantData
        };
        JobHandle queryJobHandle = queryJob.Schedule(spawnCount, 64, sortJobHandle);

        updateBoids = new UpdateBoidsJobGPU
        {
            boidConstantData = boidConstantData,
            boidSettingsData = boidSettingsData,
            Matrices = _nativeMatrices,
            scale = scale,
            angle = angle,
            deltaTime = Time.deltaTime
            };
        updateBoids.Schedule(spawnCount, 64, queryJobHandle).Complete();

        Graphics.DrawMeshInstanced(mesh, 0, mat, _nativeMatrices.ToArray(), _nativeMatrices.Length, null, UnityEngine.Rendering.ShadowCastingMode.On, true);

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
        Vector3[] rayDirections = RayCastDirections.directions;

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
    public Vector3[] GenerateConeDirections(Vector3 forward, float coneAngle, int rayCount)
    {
        Vector3[] directions = new Vector3[rayCount];
        int index = 1;

        // Asegurarse de que el forward esté normalizado
        forward.Normalize();

        // Convertir el ángulo del cono a radianes
        float coneAngleRad = Mathf.Deg2Rad * coneAngle;

        // Matriz de rotación para orientar el cono hacia el forward
        Quaternion rotation = Quaternion.LookRotation(forward);

        // Rayo central (directamente en la dirección del forward)
        directions[0] = (forward);

        // Número de anillos en el cono
        int rings = Mathf.CeilToInt(Mathf.Sqrt(rayCount));

        for (int ring = 1; ring <= rings; ring++)
        {
            // Calcular el radio del anillo basado en la progresión hacia el borde del cono
            float ringRadius = Mathf.Sin(coneAngleRad / 2f) * (ring / (float)rings);

            // Número de rayos en el anillo (proporcional al radio)
            int raysInRing = Mathf.CeilToInt(2 * Mathf.PI * ringRadius * rayCount / rings);

            for (int i = 0; i < raysInRing; i++)
            {
                // Ángulo del rayo dentro del anillo
                float theta = (i / (float)raysInRing) * 2 * Mathf.PI;

                // Coordenadas cartesianas del anillo (en espacio local)
                float x = Mathf.Cos(theta) * ringRadius;
                float y = Mathf.Sin(theta) * ringRadius;

                // Calcular z (profundidad) usando el teorema de Pitágoras
                float z = Mathf.Sqrt(1 - x * x - y * y);

                // Dirección en espacio local
                Vector3 localDirection = new Vector3(x, y, z);

                // Rotar hacia el forward usando la matriz de rotación
                Vector3 worldDirection = rotation * localDirection;

                directions[i] = worldDirection;
                index++;
            }
        }

        return directions;
    }
}

[BurstCompile]
struct GetRayCastDirections : IJobParallelFor
{
    [ReadOnly] public int numViewDirections;
    [ReadOnly] public float angleIncrement;
    [WriteOnly] public NativeArray<float3> directions;

    public void Execute(int index)
    {
        float t = (float)index / numViewDirections;
        float inclination = Mathf.Acos(1 - 2 * t);
        float azimuth = angleIncrement * index;

        float x = Mathf.Sin(inclination) * Mathf.Cos(azimuth);
        float y = Mathf.Sin(inclination) * Mathf.Sin(azimuth);
        float z = Mathf.Cos(inclination);
        directions[index] = new Vector3(x, y, z);
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
        float3 globalDirForce = SteerTowards(currentBoid.globalDirConstant, boidSettingsData.steerSpeed, currentBoid) * boidSettingsData.wanderWeight;
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

[BurstCompile]
struct GetBoidInHashJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<BoidConstantData> boids;
    [ReadOnly] public float cellSize;

    [WriteOnly]public NativeArray<HashAndIndex> hashAndIndices;


    public void Execute(int index)
    {
        BoidConstantData boid = boids[index];
        int hash = Hash(GridPosition(boid.position, cellSize));

        hashAndIndices[index] = new HashAndIndex { Hash = hash, Index = index };
    }
    public int Hash(int3 gridPos)
    {
        unchecked
        {
            return gridPos.x * 73856093 ^ gridPos.y * 19349663 ^ gridPos.z * 83492791;
        }
    }

    int3 GridPosition(float3 position, float cellSize)
    {
        return new int3(math.floor(position / cellSize));
    }
}

[BurstCompile]
struct SortHashCodesJob : IJob
{
    public NativeArray<HashAndIndex> hashAndIndices;

    public void Execute()
    {
        hashAndIndices.Sort();
    }
}

[BurstCompile]
struct QueryJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<BoidCurrentData> boidData;
    [ReadOnly] public NativeArray<HashAndIndex> hashAndIndices;
    [ReadOnly] public float cellSize;
    [ReadOnly] public bool canSteer;
    [ReadOnly] public float wanderJitter;
    [ReadOnly] public float perceptionRadius;
    [ReadOnly] public float avoidanceRadius;
    [ReadOnly] public uint seed;

    public NativeArray<BoidConstantData> boidConstantData;

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

        float radiusSquared = perceptionRadius * perceptionRadius;
        int3 minGridPos = GridPosition(currentBoid.position - perceptionRadius, cellSize);
        int3 maxGridPos = GridPosition(currentBoid.position + perceptionRadius, cellSize);

        for (int x = minGridPos.x; x <= maxGridPos.x; x++)
        {
            for (int y = minGridPos.y; y <= maxGridPos.y; y++)
            {
                for (int z = minGridPos.z; z <= maxGridPos.z; z++)
                {
                    int3 gridPos = new(x, y, z);
                    int hash = Hash(gridPos);

                    int startIndex = BinarySearchFirst(hashAndIndices, hash);

                    if (startIndex < 0) continue;

                    for (int i = startIndex; i < hashAndIndices.Length && hashAndIndices[i].Hash == hash; i++)
                    {
                        int particleIndex = hashAndIndices[i].Index;
                        BoidCurrentData otherBoid = boidData[particleIndex];

                        if(particleIndex != index)
                        {
                            float3 awayFromNeighbor = otherBoid.position - currentBoid.position;
                            float distanceFromNeightBor = math.length(awayFromNeighbor);

                            if (distanceFromNeightBor < radiusSquared)
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

    int BinarySearchFirst(NativeArray<HashAndIndex> array, int hash)
    {
        int left = 0;
        int right = array.Length - 1;
        int result = -1;

        while (left <= right)
        {
            int mid = (left + right) / 2;
            int midHash = array[mid].Hash;

            if (midHash == hash)
            {
                result = mid;
                right = mid - 1;
            }
            else if (midHash < hash)
            {
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }
        return result;
    }
    int Hash(int3 gridPos)
    {
        unchecked
        {
            return gridPos.x * 73856093 ^ gridPos.y * 19349663 ^ gridPos.z * 83492791;
        }
    }

    int3 GridPosition(float3 position, float cellSize)
    {
        return new int3(math.floor(position / cellSize));
    }
}




