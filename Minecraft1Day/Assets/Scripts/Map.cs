﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

public class Map : MonoBehaviour
{
    public static Map instance;

    [Header("World Data")]
    public const int seed = 1337;
    public System.Random random = new System.Random(seed);
    public Biome biome;

    [Header("Chunk Data")]
    public GameObject chunkPrefab;
    public Dictionary<ChunkCoordinate, Chunk> chunks = new Dictionary<ChunkCoordinate, Chunk>();
    public Dictionary<ChunkCoordinate, Chunk> outOfViewChunks = new Dictionary<ChunkCoordinate, Chunk>();
    public List<ChunkCoordinate> generateChunks = new List<ChunkCoordinate>();

    [Header("Player Data")]
    public GameObject player;
    public Vector3 spawnPosition;
    public ChunkCoordinate currentCoordinate;

    [Header("Block Data")]
    public List<BlockSO> blocksSOs = new List<BlockSO>();
    public Dictionary<BlockType, BlockSO> blocksDict = new Dictionary<BlockType, BlockSO>();

    [Header("Modifications")]
    public Queue<VoxelMod> modifications = new Queue<VoxelMod>();
    List<Chunk> chunksToUpdate = new List<Chunk>();
    bool applyingModifications = false;

    [Header("UI")]
    public bool viewingUI = false;

    [Header("VFX")]
    public VisualEffect breakingBlockEffect;


    //-----------------------------------------------------------------------------------//
    //Chunk Initialization and Update
    //-----------------------------------------------------------------------------------//
    private void Awake()
    {
        if (instance != null)
        {
            return;
        }
        instance = this;

        foreach (BlockSO blockSO in blocksSOs)
        {
            blocksDict.Add(blockSO.blockType, blockSO);
        }
    }

    private void Start()
    {
        UnityEngine.Random.InitState(seed);
        spawnPosition = new Vector3(VoxelData.MapSizeInChunks * VoxelData.ChunkWidth / 2, VoxelData.ChunkHeight - 50, VoxelData.MapSizeInChunks * VoxelData.ChunkWidth / 2);
        GenerateMap();
    }

    private void Update()
    {
        UpdateMap();
    }
    //-----------------------------------------------------------------------------------//

    //-----------------------------------------------------------------------------------//
    //Map Functions
    //-----------------------------------------------------------------------------------//
    public void GenerateMap()
    {
        int start = (VoxelData.MapSizeInChunks / 2) - VoxelData.ViewDistanceInChunks;
        int end = (VoxelData.MapSizeInChunks / 2) + VoxelData.ViewDistanceInChunks;
        for (int x = start; x < end; x++)
        {
            for (int z = start; z < end; z++)
            {
                BuildChunk(new ChunkCoordinate(x, z));
            }
        }

        while (modifications.Count > 0)
        {
            VoxelMod modification = modifications.Dequeue();
            ChunkCoordinate coordinate = GetChunkCoordinate(modification.coordinate);
            Chunk chunk = GetChunk(modification.coordinate);
            if (chunk == null) {
                chunk = BuildChunk(coordinate);
            }

            chunk.modifications.Enqueue(modification);

            if (!chunksToUpdate.Contains(chunk))
            {
                chunksToUpdate.Add(chunk);
            }
        }

        foreach (Chunk chunk in chunksToUpdate)
        {
            chunk.UpdateChunk();
        }

        player.transform.position = spawnPosition;
        currentCoordinate = GetChunkCoordinate(spawnPosition);
    }

    public void UpdateMap(bool instant = false)
    {
        ChunkCoordinate coordinate = GetChunkMidCoordinate(player.transform.position);
        if (coordinate.Equals(currentCoordinate))
        {
            UpdateMapChunks();
            return;
        }

        int xStart = coordinate.x - VoxelData.ViewDistanceInChunks;
        int xEnd = coordinate.x + VoxelData.ViewDistanceInChunks;
        int zStart = coordinate.z - VoxelData.ViewDistanceInChunks;
        int zEnd = coordinate.z + VoxelData.ViewDistanceInChunks;
        for (int x = xStart; x < xEnd; x++)
        {
            for (int z = zStart; z < zEnd; z++)
            {
                ChunkCoordinate viewCoordinate = new ChunkCoordinate(x, z);
                if (IsChunkInWorld(viewCoordinate))
                {
                    chunks.TryGetValue(viewCoordinate, out Chunk chunk);
                    outOfViewChunks.TryGetValue(viewCoordinate, out Chunk outOfViewChunk);
                    if (chunk == null && outOfViewChunk == null && !generateChunks.Contains(viewCoordinate))
                    {
                        if (instant)
                        {
                            BuildChunk(viewCoordinate);
                        }
                        else
                        {
                            generateChunks.Add(viewCoordinate);
                        }
                    }
                    if (outOfViewChunk != null)
                    {
                        outOfViewChunk.Toggle(true);
                        chunks[viewCoordinate] = outOfViewChunk;
                        outOfViewChunks.Remove(viewCoordinate);
                    }
                }
            }
        }

        // Remove Chunks that are far away
        List<ChunkCoordinate> removeCoordinates = new List<ChunkCoordinate>();
        foreach (KeyValuePair<ChunkCoordinate, Chunk> chunkPair in chunks)
        {
            ChunkCoordinate removeCoordinate = chunkPair.Key;
            if (Mathf.Abs(coordinate.x - removeCoordinate.x) > VoxelData.ViewDistanceInChunks ||
                Mathf.Abs(coordinate.z - removeCoordinate.z) > VoxelData.ViewDistanceInChunks)
            {
                Chunk removeChunk = chunkPair.Value;
                removeChunk.Toggle(false);
                outOfViewChunks[removeCoordinate] = removeChunk;
            }
        }

        foreach (ChunkCoordinate removeCoordinate in removeCoordinates)
        {
            chunks.Remove(removeCoordinate);
        }
        currentCoordinate = coordinate;

        UpdateMapChunks();
    }

    public void UpdateMapChunks()
    {
        if (modifications.Count > 0 && !applyingModifications)
        {
            StartCoroutine(ApplyModifications());
        }

        if (generateChunks.Count > 0)
        {
            chunks.TryGetValue(generateChunks[0], out Chunk chunk);
            if (chunk == null)
            {
                BuildChunk(generateChunks[0]);
            }
            generateChunks.RemoveAt(0);
        }

        if (chunksToUpdate.Count > 0)
        {
            UpdateChunks();
        }
    }

    public Chunk BuildChunk(ChunkCoordinate coordinate)
    {
        GameObject chunkGO = Instantiate(chunkPrefab, new Vector3(coordinate.x, 0, coordinate.z), Quaternion.identity);
        Chunk chunk = chunkGO.GetComponent<Chunk>();
        chunk.Initialize(coordinate);
        chunks.Add(coordinate, chunk);

        return chunk;
    }

    public void UpdateChunks()
    {
        bool updated = false;
        int index = 0;
        while (!updated && index < chunksToUpdate.Count - 1)
        {
            Chunk updateChunk = chunksToUpdate[index];
            if (updateChunk.initialized)
            {
                updateChunk.UpdateChunk();
                chunksToUpdate.Remove(updateChunk);
                updated = true;
            }
            index += 1;
        }
    }

    private IEnumerator ApplyModifications()
    {
        applyingModifications = true;
        int count = 0;

        while (modifications.Count > 0)
        {
            VoxelMod modification = modifications.Dequeue();
            ChunkCoordinate coordinate = GetChunkCoordinate(modification.coordinate);
            Chunk chunk = GetChunk(modification.coordinate);
            if (chunk == null)
            {
                chunk = BuildChunk(coordinate);
            }

            chunk.modifications.Enqueue(modification);

            if (!chunksToUpdate.Contains(chunk))
            {
                chunksToUpdate.Add(chunk);
            }

            // Limit voxel modifications to 200 per frame
            count += 1;
            if (count > 200)
            {
                count = 0;
                yield return null;
            }
        }

        applyingModifications = false;
    }
    //-----------------------------------------------------------------------------------//

    //-----------------------------------------------------------------------------------//
    //Voxel Functions
    //-----------------------------------------------------------------------------------//
    public BlockType GetExistingVoxel(int x, int y, int z)
    {
        Vector3 position = new Vector3(x, y, z);
        ChunkCoordinate chunkCoordinate = GetChunkCoordinate(position);
        if (!IsChunkInWorld(chunkCoordinate) || y < 0 || y > VoxelData.ChunkHeight)
        {
            return BlockType.Air;
        }

        chunks.TryGetValue(chunkCoordinate, out Chunk chunk);
        if (chunk != null && chunk.initialized)
        {
            return chunk.GetVoxel(position);
        }

        return GetNewVoxel(x, y, z);
    }

    public BlockType GetNewVoxel(int x, int y, int z)
    {
        // If outside the world, return air
        if (!IsVoxelInWorld(x, y, z))
        {
            return BlockType.Air;
        }

        // If bottom of chunk, return bedrock
        if (y == 0)
        {
            return BlockType.Bedrock;
        }

        BlockType blockType = BlockType.Air;

        // Noise
        float heightNoise = Noise.Get2DPerlin(new Vector2(x, z), 0, biome.terrainScale);

        // 1st Terrain Pass
        int terrainHeight = (int)(biome.terrainHeight * heightNoise) + biome.solidGroundHeight;
        if (y == terrainHeight)
        {
            blockType = BlockType.Grass;
        }
        else if (y < terrainHeight && y > terrainHeight - 4)
        {
            blockType = BlockType.Dirt;
        }
        else if (y > terrainHeight)
        {
            return BlockType.Air;
        }
        else
        {
            foreach (Lode lode in biome.lodes)
            {
                if (y > lode.minHeight &&
                    y < lode.maxHeight &&
                    Noise.Get3DPerlin(new Vector3(x, y, z), lode.noiseOffset, lode.scale, lode.threshold))
                {
                    blockType = lode.blockType;
                }
            }
        }

        // 2nd Terrain Pass
        Vector2 coordinate = new Vector2(x, z);
        if (y == terrainHeight && terrainHeight > biome.treeZoneHeight)
        {            
            if (Noise.Get2DPerlin(coordinate, 0, biome.treeZoneScale) > biome.treeZoneThreshold)
            {
                blockType = BlockType.Grass;
                if (Noise.Get2DPerlin(coordinate, 0, biome.treePlacementScale) > biome.treePlacementThreshold)
                {
                    blockType = BlockType.Wood;
                    Structure.CreateTree(new Vector3(x, y, z), modifications, biome.minTreeHeight, biome.maxTreeHeight);
                }
                return blockType;
            }
        }


        // 3rd Terrain Pass
        if (y == terrainHeight && terrainHeight <= biome.waterHeight && terrainHeight > biome.waterHeight - biome.sandDepth)
        {
            blockType = BlockType.Sand;
            return blockType;
        }
        else if (y == terrainHeight && terrainHeight <= biome.waterHeight - biome.sandDepth)
        {
            if (Noise.Get2DPerlin(coordinate, 0, 1) > biome.waterSandZoneThreshold)
            {
                blockType = BlockType.Sand;
            }
            else
            {
                blockType = BlockType.Dirt;
            }
            return blockType;
        }

        // 4th Terrain Pass
        if (y == terrainHeight && terrainHeight > biome.stoneheight)
        {
            if (Noise.Get2DPerlin(coordinate, 0, 1) > biome.stoneZoneThreshold)
            {
                blockType = BlockType.Stone;
                return blockType;
            }
        }            

        return blockType;
    }
    //-----------------------------------------------------------------------------------//


    //-----------------------------------------------------------------------------------//
    //Check Functions
    //-----------------------------------------------------------------------------------//
    public ChunkCoordinate GetChunkCoordinate(Vector3 position)
    {
        int x = (int)position.x / VoxelData.ChunkWidth;
        int z = (int)position.z / VoxelData.ChunkWidth;
        return new ChunkCoordinate(x, z);
    }


    public Chunk GetChunk(Vector3 position)
    {
        ChunkCoordinate chunkCoordinate = GetChunkCoordinate(position);
        chunks.TryGetValue(chunkCoordinate, out Chunk chunk);
        return chunk;
    }

    // Used for correct map view distance updating
    public ChunkCoordinate GetChunkMidCoordinate(Vector3 position)
    {
        int x = ((int)position.x + (VoxelData.ChunkWidth / 2)) / VoxelData.ChunkWidth;
        int z = ((int)position.z + (VoxelData.ChunkWidth / 2)) / VoxelData.ChunkWidth;
        return new ChunkCoordinate(x, z);
    }

    public bool IsChunkInWorld(ChunkCoordinate coordinate)
    {
        if (coordinate.x > 0 && coordinate.x < VoxelData.MapSizeInChunks &&
            coordinate.z > 0 && coordinate.z < VoxelData.MapSizeInChunks)
        {
            return true;
        }
        return false;
    }

    public bool IsVoxelInWorld(int x, int y, int z)
    {
        if (x >= 0 && x < VoxelData.MapSizeInVoxels &&
            y >= 0 && y < VoxelData.ChunkHeight &&
            z >= 0 && z < VoxelData.MapSizeInVoxels)
        {
            return true;
        }
        return false;
    }

    public bool IsSolid(float x, float y, float z)
    {
        BlockType blockType = GetExistingVoxel((int)x, (int)y, (int)z);
        return blockType != BlockType.Air;
    }
    //-----------------------------------------------------------------------------------//

    //-----------------------------------------------------------------------------------//
    //UI Functions
    //-----------------------------------------------------------------------------------//
    public void ToggleUI(bool toggle)
    {
        viewingUI = toggle;
    }
    //-----------------------------------------------------------------------------------//
}
