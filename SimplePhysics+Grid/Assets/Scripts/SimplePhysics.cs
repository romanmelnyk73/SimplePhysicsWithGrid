using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class SimplePhysics : MonoBehaviour
{
    public struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public Color color;
        public Vector3 force;

        public Particle(float posRange, float maxVel)
        {
            position.x = Random.value * posRange / 2;
            position.y = (Random.value - 0.5f) * posRange;
            position.z = Random.value * posRange / 2;
            velocity.x = Random.value * maxVel - maxVel / 2;
            velocity.y = Random.value * maxVel - maxVel / 2;
            velocity.z = Random.value * maxVel - maxVel / 2;
            velocity = Vector3.zero;
            //color.r = Random.value;
            //color.g = Random.value;
            //color.b = Random.value;
            //color.a = 1;
            //color = new Vector4(0.0f, 0.4f, 0.5f, 1.0f);
            color = Vector4.one;
            force = Vector3.zero;    
        }
    }

    public ComputeShader shader;
    public Mesh particleMesh;
    public Material particleMaterial;
    public float particleMass;
    public float frictionCoefficient;
    public float gravityCoefficient;

    public float springCoefficient;
    public float dampingCoefficient;
    public float tangentialCoefficient;
    public float linearForceScalar;

    public int particlesCount;
    public float particleDiameter = 0.2f;
    public float boxSize = 2.5f;
    float radius;

    ComputeBuffer particlesBuffer;
    private ComputeBuffer voxelGridBuffer;                 // int4
    ComputeBuffer argsBuffer;
    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    Particle[] particlesArray;
    int[] voxelGridArray;
    int[] gridDimensions;
    int groupSizeX;
    int numOfParticles;
    int numGridCells;
    Bounds bounds;

    int kernelHandle;
    private int kernelClearGrid;
    private int kernelPopulateGrid;
    private int kernelCollisionDetection;
    private int groupsPerGridCell;
    public Vector3Int gridSize = new Vector3Int(5, 5, 5);
	public Vector3 gridPosition;//centre of grid
    private Vector3 pos;
    private Vector3 halfSize;
    public bool useGrid = true;


    MaterialPropertyBlock props;

    void Start()
    {
        radius = 0.5f * particleDiameter;
        gridSize.x = Mathf.CeilToInt(gridSize.x / (2 * radius));
        gridSize.y = Mathf.CeilToInt(gridSize.y / (2 * radius));
        gridSize.z = Mathf.CeilToInt(gridSize.z / (2 * radius));

        groupsPerGridCell = Mathf.CeilToInt((gridSize.x * gridSize.y * gridSize.z) / 8f);

        kernelClearGrid = shader.FindKernel("ClearGrid");
	    kernelPopulateGrid = shader.FindKernel("PopulateGrid");
        kernelHandle = shader.FindKernel("CollisionDetectionUsingGrid");
        kernelCollisionDetection = shader.FindKernel("CollisionDetection");
    
        uint x;
        shader.GetKernelThreadGroupSizes(kernelHandle, out x, out _, out _);
        groupSizeX = Mathf.CeilToInt((float)particlesCount / (float)x);
        numOfParticles = groupSizeX * (int)x;
        
        props = new MaterialPropertyBlock();
        props.SetFloat("_UniqueID", Random.value);

        bounds = new Bounds(Vector3.zero, Vector3.one * 1000);

        InitParticles();
        InitShader();
    }

    private void InitParticles()
    {
        particlesArray = new Particle[numOfParticles];

        for (int i = 0; i < numOfParticles; i++)
        {
            particlesArray[i] = new Particle(5, 1.01f);
        }
         voxelGridArray = new int[gridSize.x * gridSize.y * gridSize.z * 8];
    }

    void InitShader()
    {
        particlesBuffer = new ComputeBuffer(numOfParticles, 13 * sizeof(float));
        particlesBuffer.SetData(particlesArray);

        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        if (particleMesh != null)
        {
            args[0] = (uint)particleMesh.GetIndexCount(0);
            args[1] = (uint)numOfParticles;
            args[2] = (uint)particleMesh.GetIndexStart(0);
            args[3] = (uint)particleMesh.GetBaseVertex(0);
        }
        argsBuffer.SetData(args);

        numGridCells = gridSize.x * gridSize.y * gridSize.z;
		voxelGridBuffer = new ComputeBuffer(numGridCells, 8 * sizeof(int));

        gridDimensions = new int[3] {gridSize.x, gridSize.y, gridSize.z};
        shader.SetInts("gridDimensions", gridDimensions);
		shader.SetInt("gridMax", numGridCells);
        shader.SetInt("particlesCount", numOfParticles);
        shader.SetFloat("particleDiameter", particleDiameter);
        shader.SetFloat("particleMass", particleMass);

        Vector3 halfSize = new Vector3(gridSize.x, gridSize.y, gridSize.z) * particleDiameter * 0.5f;
        Vector3 pos = gridPosition * particleDiameter - halfSize;
        shader.SetVector("gridStartPosition", pos);


        // Bind buffers

        // kernel 1 ClearGrid
        shader.SetBuffer(kernelClearGrid, "voxelGridBuffer", voxelGridBuffer);

		// // kernel 2 Populate Grid
		shader.SetBuffer(kernelPopulateGrid, "voxelGridBuffer", voxelGridBuffer);
		shader.SetBuffer(kernelPopulateGrid, "particlesBuffer", particlesBuffer);
		
		// kernel 3 Collision Detection using Grid
		shader.SetBuffer(kernelHandle, "particlesBuffer", particlesBuffer);
		shader.SetBuffer(kernelHandle, "voxelGridBuffer", voxelGridBuffer);

        // kernel 3 Collision Detection without Grid
        shader.SetBuffer(kernelCollisionDetection, "particlesBuffer", particlesBuffer);
        //shader.SetBuffer(kernelCollisionDetection, "voxelGridBuffer", voxelGridBuffer);


        shader.SetVector("limitsXZ", new Vector4(-boxSize+radius, boxSize-radius, -boxSize+radius, boxSize-radius));
        shader.SetFloat("floorY", -boxSize+radius);
        shader.SetFloat("radius", radius);

        shader.SetFloat("particleMass", particleMass);
        shader.SetFloat("springCoefficient", springCoefficient);
		shader.SetFloat("dampingCoefficient", dampingCoefficient);
		shader.SetFloat("frictionCoefficient", frictionCoefficient);

		shader.SetFloat("gravityCoefficient", gravityCoefficient);
		shader.SetFloat("tangentialCoefficient", tangentialCoefficient);
		shader.SetFloat("linearForceScalar", linearForceScalar);

        particleMaterial.SetFloat("_Radius", radius*2);
        particleMaterial.SetBuffer("particlesBuffer", particlesBuffer);
    }

    void Update()
    {
        int iterations = 5;
        shader.SetFloat("deltaTime", Time.deltaTime/iterations);

        for (int i = 0; i < iterations; i++)
        {
            if (useGrid)
            {
                shader.Dispatch(kernelClearGrid, groupsPerGridCell, 1, 1);
                shader.Dispatch(kernelPopulateGrid, groupSizeX, 1, 1);
                shader.Dispatch(kernelHandle, groupSizeX, 1, 1);
            }
            else
            {
                shader.Dispatch(kernelCollisionDetection, groupSizeX, 1, 1);
            }
        }
        //voxelGridBuffer.GetData(voxelGridArray);
        //int kl = voxelGridArray.Length;
        //int j = 0;
        //for (int i = 0; i < kl; i++)
        //{
        //    if (voxelGridArray[i] != 0 && voxelGridArray[i] != -1)
        //    {
        //        j += 1;
        //    }
        //}
        //Debug.Log("output=" + kl + "idx=" + j);

        Graphics.DrawMeshInstancedIndirect(particleMesh, 0, particleMaterial, bounds, argsBuffer, 0, props);
    }

    void OnDestroy()
    {
        if (particlesBuffer != null)
        {
            particlesBuffer.Dispose();
        }

        if (voxelGridBuffer != null)
        {
            voxelGridBuffer.Dispose();
        }

        if (argsBuffer != null)
        {
            argsBuffer.Dispose();
        }
    }
}

