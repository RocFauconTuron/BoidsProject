using System.Collections.Generic;
using UnityEngine;

public class BoidManager : MonoBehaviour 
{
    List<Boid> boids;
    public Boid prefab;
    public float spawnRadius = 10;
    public int spawnCount = 10;
    bool doneSpawning = false;
    float timer = 0;

    public Vector3 bounds;
    void Awake()
    {
        boids = new List<Boid>();
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * spawnRadius;
            Boid boid = Instantiate(prefab);
            boid.transform.position = pos;
            boid.transform.forward = Random.insideUnitSphere;
            boids.Add(boid);
            boids[i].Initialize();
        }
        timer = 100000;
        doneSpawning = true;
    }

    void Update () 
    {
        if (!doneSpawning) return;

        int numBoids = boids.Count;
        BoidData[] boidData = new BoidData[numBoids];
        bool canSteer = false;


        if (timer > Random.Range(boids[0].timeMinWander, boids[0].timeMaxWander))
        {
            canSteer = true;
            timer = 0;
        }
        timer += Time.deltaTime;


        for (int i = 0; i < numBoids; i++)
        {
            boidData[i].position = boids[i].position;
            boidData[i].direction = boids[i].forward;
            if (canSteer)
            {
                boids[i].globalDirConstant = Vector3.zero;               
            }
        }

        for (int i = 0; i < numBoids; i++)
        {
            if (canSteer)
            {
                float groupDirJitter = boids[i].wanderJitter;
                boidData[i].globalDir = new Vector3(Random.Range(-groupDirJitter, groupDirJitter), Random.Range(-groupDirJitter, groupDirJitter), Random.Range(-groupDirJitter, groupDirJitter));
            }

            for (int j = 0; j < numBoids; j++)
            {
                if (i != j)
                {
                    BoidData otherBoid = boidData[j];
                    Vector3 awayFromNeighbor = otherBoid.position - boidData[i].position;

                    if (awayFromNeighbor.magnitude < boids[i].perceptionRadius * boids[i].perceptionRadius)
                    {
                        boidData[i].numNeightbors += 1;
                        boidData[i].flockHeading += otherBoid.direction;
                        boidData[i].flockCentre += otherBoid.position;
                        if (canSteer)
                        {
                            if (otherBoid.globalDir != Vector3.zero)
                            {
                                boidData[i].globalDir = otherBoid.globalDir;
                            }
                        }                      

                        if (awayFromNeighbor.magnitude < boids[i].avoidanceRadius * boids[i].avoidanceRadius)
                        {
                            boidData[i].flockSeparation -= awayFromNeighbor.normalized;                    
                        }
                    }
                }
            }
        }


        for (int i = 0; i < boids.Count; i++)
        {
            boids[i].avgFlockDirection = boidData[i].flockHeading;
            boids[i].centreOfFlockmates = boidData[i].flockCentre;
            boids[i].avgSeparationDirection = boidData[i].flockSeparation;
            boids[i].numPerceivedFlockmates = boidData[i].numNeightbors;
            if (canSteer)
            {
                boids[i].globalDirConstant = boidData[i].globalDir;
            }
            boids[i].UpdateBoid();
            //if (boids[i].transform.position.x > bounds.x)
            //{
            //    boids[i].transform.position = new Vector3(-bounds.x, boids[i].position.y, boids[i].position.z);
            //}
            //if (boids[i].transform.position.y > bounds.y)
            //{
            //    boids[i].transform.position = new Vector3(boids[i].position.x, -bounds.y, boids[i].position.z);
            //}
            //if (boids[i].transform.position.z > bounds.z)
            //{
            //    boids[i].transform.position = new Vector3(boids[i].position.x, boids[i].position.y, -bounds.z);
            //}


            //if (boids[i].transform.position.x < -bounds.x)
            //{
            //    boids[i].transform.position = new Vector3(bounds.x, boids[i].position.y, boids[i].position.z);
            //}
            //if (boids[i].transform.position.y < -bounds.y)
            //{
            //    boids[i].transform.position = new Vector3(boids[i].position.x, bounds.y, boids[i].position.z);
            //}
            //if (boids[i].transform.position.z < -bounds.z)
            //{
            //    boids[i].transform.position = new Vector3(boids[i].position.x, boids[i].position.y, bounds.z);
            //}
            //boids[i].position = boids[i].transform.position;
        }         
        
    }

    //void OnDrawGizmos()
    //{
    //    // Dibujar la esfera
    //    Gizmos.color = new Color(0, 1, 0, 0.2f);
    //    Gizmos.DrawSphere(pos, boids[0].avoidanceRadius * boids[0].avoidanceRadius);

    //    // Dibujar las líneas
    //    Gizmos.color = Color.red;
    //    for (int i = 0; i < neightBorpos.Count; i++)
    //    {
    //        Gizmos.DrawLine(pos, neightBorpos[i]);
    //    }
    //}

    public struct BoidData 
    {
        public Vector3 position;
        public Vector3 direction;
        public Vector3 globalDir;

        public Vector3 flockHeading;
        public Vector3 flockCentre;
        public Vector3 flockSeparation;
        public int numNeightbors;
    }
}