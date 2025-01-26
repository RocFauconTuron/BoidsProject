using UnityEngine;
using System.Collections.Generic;

public class RaycastCone : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        List<Vector3> directions = GenerateConeDirections(100, 50);

        foreach (Vector3 dir in directions)
        {
            Debug.DrawRay(Vector3.zero, dir * 5f, Color.red, 5f); // Dibuja los rayos para visualización
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public List<Vector3> GenerateConeDirections(float coneAngle, int rayCount)
    {
        List<Vector3> directions = new List<Vector3>();

        // Convertir el ángulo a radianes y calcular el ángulo máximo desde el eje central
        float coneAngleRad = Mathf.Deg2Rad * coneAngle;
        float maxRadius = Mathf.Sin(coneAngleRad / 2f);

        directions.Add(Vector3.forward); // Rayo central

        int rings = Mathf.CeilToInt(Mathf.Sqrt(rayCount)); // Calcular anillos según la densidad deseada

        for (int ring = 1; ring <= rings; ring++)
        {
            // Radio del anillo basado en la progresión hacia el borde del cono
            float ringRadius = maxRadius * (ring / (float)rings);

            // Número de rayos en el anillo (proporcional al radio)
            int raysInRing = Mathf.CeilToInt(2 * Mathf.PI * ringRadius * rayCount / rings);

            for (int i = 0; i < raysInRing; i++)
            {
                float theta = (i / (float)raysInRing) * 2 * Mathf.PI; // Ángulo del rayo en el anillo
                float x = Mathf.Cos(theta) * ringRadius;
                float y = Mathf.Sin(theta) * ringRadius;
                float z = Mathf.Sqrt(1 - x * x - y * y); // Calcular z para mantener el vector unitario

                directions.Add(new Vector3(x, y, z));
            }
        }

        return directions;
    }
}
