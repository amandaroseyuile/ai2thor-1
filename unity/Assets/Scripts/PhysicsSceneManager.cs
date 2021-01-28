﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// using UnityEditor;
// using System.Linq;
using UnityStandardAssets.Characters.FirstPerson;
using System;
using System.Linq;
using UnityStandardAssets.ImageEffects;
using UnityEngine.SceneManagement;

[ExecuteInEditMode]

public class PhysicsSceneManager : MonoBehaviour {

    public List<GameObject> RequiredObjects = new List<GameObject>();

    // get references to the spawned Required objects after spawning them for the first time.
    public List<GameObject> SpawnedObjects = new List<GameObject>();
    public Dictionary<string, SimObjPhysics> ObjectIdToSimObjPhysics = new Dictionary<string, SimObjPhysics>();
    public GameObject HideAndSeek;
    public GameObject[] ManipulatorTables;
    public GameObject[] ManipulatorReceptacles;
    public GameObject[] ManipulatorBooks;
    public bool AllowDecayTemperature = true; // if true, temperature of sim objects decays to Room Temp over time

    // public List<SimObjPhysics> LookAtThisList = new List<SimObjPhysics>();
    #if UNITY_EDITOR
        private bool m_Started = false;
    #endif
    
    private Vector3 gizmopos;
    private Vector3 gizmoscale;
    private Quaternion gizmoquaternion;

    // keep track of if the physics autosimulation has been paused or not
    public bool physicsSimulationPaused = false;

    // this is used to report if the scene is at rest in metadata, and also to automatically resume Physics Autosimulation if
    // physics simulation was paused
    public bool isSceneAtRest; // if any object in the scene has a non zero velocity, set to false
    public List<Rigidbody> rbsInScene = null; // list of all active rigidbodies in the scene

    private void OnEnable() {
        // clear this on start so that the CheckForDuplicates function doesn't check pre-existing lists
        SetupScene();

        if (GameObject.Find("HideAndSeek")) {
            HideAndSeek = GameObject.Find("HideAndSeek");
        }

        if (!GameObject.Find("Objects")) {
            GameObject c = new GameObject("Objects");
            Debug.Log(c.transform.name + " was missing and is now added");
        }
    }

    public void SetupScene() {
        ObjectIdToSimObjPhysics.Clear();
        GatherSimObjPhysInScene();
        GatherAllRBsInScene();
    }
    // Use this for initialization
    void Start () {
        GatherAllRBsInScene();
    }

    private void GatherAllRBsInScene() {
        // cache all rigidbodies that are in the scene by default
        // NOTE: any rigidbodies created from actions such as Slice/Break or spawned in should be added to this!
        rbsInScene = new List<Rigidbody>(FindObjectsOfType<Rigidbody>());
    }
    
    void LateUpdate() {
        // check what objects in the scene are currently in motion
        // Rigidbody[] rbs = FindObjectsOfType(typeof(Rigidbody)) as Rigidbody[];
        foreach (Rigidbody rb in rbsInScene) {
            if (rb == null) {
                return;
            }

            // if this rigidbody is part of a SimObject, calculate rest using lastVelocity/currentVelocity comparisons
            // make sure the object is actually active, otherwise skip the check
            if (rb.GetComponentInParent<SimObjPhysics>() && rb.transform.gameObject.activeSelf) {
                SimObjPhysics sop = rb.GetComponentInParent<SimObjPhysics>();
                
                float currentVelocity = Math.Abs(rb.angularVelocity.sqrMagnitude + rb.velocity.sqrMagnitude);
                float accel = (currentVelocity - sop.lastVelocity) / Time.fixedDeltaTime;

                if (Mathf.Abs(accel) <= 0.0001f) {
                    sop.inMotion = false;
                    // print(sop.transform.name + " should be sleeping");
                    // rb.Sleep(); maybe do something to ensure object has stopped moving, and reduce jitter
                } else {
                    // the rb's velocities are not 0, so it is in motion and the scene is not at rest
                    rb.GetComponentInParent<SimObjPhysics>().inMotion = true;
                    isSceneAtRest = false;
                    // #if UNITY_EDITOR
                    // print(rb.GetComponentInParent<SimObjPhysics>().name + " is still in motion!");
                    // #endif
                }
            } else {
                // this rigidbody is not a SimOBject, and might be a piece of a shattered sim object spawned in, or something
                if (rb.transform.gameObject.activeSelf) {
                    // is the rigidbody at non zero velocity? then the scene is not at rest
                    if (Math.Abs(rb.angularVelocity.sqrMagnitude + rb.velocity.sqrMagnitude) >= 0.01) {
                        isSceneAtRest = false;
                        // make sure the rb's drag values are not at 0 exactly
                        // if (rb.drag < 0.1f)
                        rb.drag += 0.01f;

                        // if (rb.angularDrag < 0.1f)
                        // rb.angularDrag = 1.5f;
                        rb.angularDrag += 0.01f;

                        #if UNITY_EDITOR
                            print(rb.transform.name + " is still in motion!");
                        #endif
                    } else {
                        // the velocities are small enough, assume object has come to rest and force this one to sleep
                        rb.drag = 1.0f;
                        rb.angularDrag = 1.0f;
                    }

                    // if the shard/broken piece gets out of bounds somehow and begins falling forever, get rid of it with this check
                    if (rb.transform.position.y < -50f) {
                        rb.transform.gameObject.SetActive(false);
                        // note: we might want to remove these from the list of rbs at some point but for now it'll be fine
                    }
                }
            }
        }
    }

    // used to add a reference to a rigidbody created after the scene was started
    public void AddToRBSInScene(Rigidbody rb) {
        rbsInScene.Add(rb);
    }

    public void RemoveFromRBSInScene(Rigidbody rb) {
        rbsInScene.Remove(rb);
    }

    public bool ToggleHideAndSeek(bool hide) {
        if (HideAndSeek) {
            if (HideAndSeek.activeSelf != hide) {
                HideAndSeek.SetActive(hide);
                SetupScene();
            }
            return true;
        }
        #if UNITY_EDITOR
            Debug.Log("Hide and Seek object reference not set!");
        #endif

        return false;
    }

    public void ResetObjectIdToSimObjPhysics() {
        ObjectIdToSimObjPhysics.Clear();
        foreach (SimObjPhysics so in GameObject.FindObjectsOfType<SimObjPhysics>()) {
            ObjectIdToSimObjPhysics[so.ObjectID] = so;
        }
    }

    public void GatherSimObjPhysInScene() {
        List<SimObjPhysics> allPhysObjects = new List<SimObjPhysics>();

        allPhysObjects.AddRange(FindObjectsOfType<SimObjPhysics>());
        allPhysObjects.Sort((x, y) => (x.Type.ToString().CompareTo(y.Type.ToString())));

        foreach(SimObjPhysics o in allPhysObjects) {
            Generate_ObjectID(o);

            // debug in editor, make sure no two object share ids for some reason
            #if UNITY_EDITOR
                if (CheckForDuplicateObjectIDs(o)) {
                    Debug.Log("Yo there are duplicate ObjectIDs! Check" + o.ObjectID + "in scene "+ UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);    
                } else {
                    AddToObjectsInScene(o);
                    continue;
                }
            #endif

            AddToObjectsInScene(o);
        }

        BaseFPSAgentController fpsController =  GameObject.FindObjectOfType<BaseFPSAgentController>();
        if (fpsController.imageSynthesis != null) {
            fpsController.imageSynthesis.OnSceneChange();
        }
    }

    public List<SimObjPhysics> GatherAllReceptaclesInScene() {
        List<SimObjPhysics> ReceptaclesInScene = new List<SimObjPhysics>();

        foreach (SimObjPhysics sop in ObjectIdToSimObjPhysics.Values) {
            if (sop.DoesThisObjectHaveThisSecondaryProperty(SimObjSecondaryProperty.Receptacle)) {
                ReceptaclesInScene.Add(sop);

                #if UNITY_EDITOR
                    // debug if some of these receptacles were not set up correctly
                    foreach (GameObject go in sop.ReceptacleTriggerBoxes) {
                        if (go == null) {
                            Debug.LogWarning(sop.gameObject + " has non-empty receptacle trigger boxes but contains a null value.");
                            continue;
                        }
                        Contains c = go.GetComponent<Contains>();
                        // c.CurrentlyContainedObjects().Clear();
                        // c.GetComponent<Collider>().enabled = false;
                        // c.GetComponent<Collider>().enabled = true;
                        if (c == null) {
                            Debug.LogWarning(sop.gameObject + " is missing a contains script on one of its receptacle boxes.");
                            continue;
                        }
                        if (go.GetComponent<Contains>().myParent == null) {
                            go.GetComponent<Contains>().myParent = sop.transform.gameObject;
                        }
                    }
                #endif
            }
        }

        ReceptaclesInScene.Sort((r0, r1) => (r0.gameObject.GetInstanceID().CompareTo(r1.gameObject.GetInstanceID())));
        return ReceptaclesInScene;
    }
    
    public void Generate_ObjectID(SimObjPhysics o) {
        // check if this object requires it's parent simObjs ObjectID as a prefix
        if (ReceptacleRestrictions.UseParentObjectIDasPrefix.Contains(o.Type)) {
            SimObjPhysics parent = o.transform.parent.GetComponent<SimObjPhysics>();
            if (parent == null) {
                Debug.LogWarning("Object " + o + " requires a SimObjPhysics " +
                "parent to create its object ID but none exists. Using 'None' instead.");
                o.ObjectID = "None|" + o.Type.ToString();
                return;
            }

            if (parent.ObjectID == null) {
                Vector3 ppos = parent.transform.position;
                string xpPos = (ppos.x >= 0 ? "+" : "") + ppos.x.ToString("00.00");
                string ypPos = (ppos.y >= 0 ? "+" : "") + ppos.y.ToString("00.00");
                string zpPos = (ppos.z >= 0 ? "+" : "") + ppos.z.ToString("00.00");
                parent.ObjectID = parent.Type.ToString() + "|" + xpPos + "|" + ypPos + "|" + zpPos;
            }

            o.ObjectID = parent.ObjectID + "|" + o.Type.ToString();
            return;

        }

        Vector3 pos = o.transform.position;
        string xPos = (pos.x >= 0 ? "+" : "") + pos.x.ToString("00.00");
        string yPos = (pos.y >= 0 ? "+" : "") + pos.y.ToString("00.00");
        string zPos = (pos.z >= 0 ? "+" : "") + pos.z.ToString("00.00");
        o.ObjectID = o.Type.ToString() + "|" + xPos + "|" + yPos + "|" + zPos;
    }

    // used to create object id for an object created as result of a state change of another object ie: bread - >breadslice1, breadslice 2 etc
    public void Generate_InheritedObjectID(SimObjPhysics sourceObject, SimObjPhysics createdObject, int count) {
        createdObject.ObjectID = sourceObject.ObjectID + "|" + createdObject.ObjType + "_" + count;
        AddToObjectsInScene(createdObject);
    }
    
    private bool CheckForDuplicateObjectIDs(SimObjPhysics sop) {
        return ObjectIdToSimObjPhysics.ContainsKey(sop.ObjectID);
    }

    public void AddToObjectsInScene(SimObjPhysics sop) {
        ObjectIdToSimObjPhysics[sop.ObjectID] = sop;
        if (sop.GetComponent<Rigidbody>()) {
            Rigidbody rb = sop.GetComponent<Rigidbody>();
            AddToRBSInScene(rb);
        }
    }

    public void RemoveFromObjectsInScene(SimObjPhysics sop) {
        if (ObjectIdToSimObjPhysics.ContainsKey(sop.ObjectID)) {
            ObjectIdToSimObjPhysics.Remove(sop.ObjectID);
            if (sop.GetComponent<Rigidbody>()) {
                Rigidbody rb = sop.GetComponent<Rigidbody>();
                RemoveFromRBSInScene(rb);
            }
        }
    }

    public void RemoveFromSpawnedObjects(SimObjPhysics sop) {
        SpawnedObjects.Remove(sop.gameObject);
    }

    public void RemoveFromRequiredObjects(SimObjPhysics sop) {
        RequiredObjects.Remove(sop.gameObject);
    }

    public bool SetObjectPoses(ObjectPose[] objectPoses) {
        SetupScene();
        bool shouldFail = false;
        if (objectPoses != null && objectPoses.Length > 0) {
            // Perform object location sets
            SimObjPhysics[] sceneObjects = FindObjectsOfType<SimObjPhysics>();
            Dictionary<string, SimObjPhysics> nameToObject = new Dictionary<string, SimObjPhysics>();
            foreach (SimObjPhysics sop in sceneObjects) {
                if (sop.IsPickupable) {
                    sop.gameObject.SetActive(false);
                    // sop.gameObject.GetComponent<SimpleSimObj>().IsDisabled = true;
                    nameToObject[sop.name] = sop;
                }
            }
            HashSet<SimObjPhysics> placedOriginal = new HashSet<SimObjPhysics>();
            for (int ii = 0; ii < objectPoses.Length; ii++) {
                ObjectPose objectPose = objectPoses[ii];
                if (!nameToObject.ContainsKey(objectPose.objectName)) {
                    Debug.Log("No object of name " + objectPose.objectName + " found in scene.");
                    shouldFail = true;
                    continue;
                }
                SimObjPhysics obj = nameToObject[objectPose.objectName];
                SimObjPhysics existingSOP = obj.GetComponent<SimObjPhysics>();
                SimObjPhysics copy;
                if (placedOriginal.Contains(existingSOP)) {
                    copy = Instantiate(existingSOP);
                    copy.name += "_copy_" + ii;
                    copy.ObjectID = existingSOP.ObjectID + "_copy_" + ii;
                    copy.objectID = copy.ObjectID;
                } else {
                    copy = existingSOP;
                    placedOriginal.Add(existingSOP);
                }

                copy.transform.position = objectPose.position;
                copy.transform.eulerAngles = objectPose.rotation;
                copy.gameObject.SetActive(true);
                // copy.GetComponent<SimpleSimObj>().IsDisabled = false;
            }
        }
        SetupScene();
        return !shouldFail;
    }

    public System.Collections.Generic.IEnumerator<SimObjPhysics> GetValidReceptaclesForSimObj(
        SimObjPhysics simObj, List<SimObjPhysics> receptaclesInScene
    ) {
        SimObjType goObjType = simObj.ObjType;
        bool typeFoundInDictionary = ReceptacleRestrictions.PlacementRestrictions.ContainsKey(goObjType);
        if (typeFoundInDictionary) {
            List<SimObjType> typesOfObjectsPrefabIsAllowedToSpawnIn = new List<SimObjType>(ReceptacleRestrictions.PlacementRestrictions[goObjType]);

            // remove from list if receptacle isn't in this scene
            // compare to receptacles that exist in scene, get the ones that are the same
            foreach(SimObjPhysics receptacleSop in receptaclesInScene) {
                // don't random spawn in objects that are pickupable to prevent Egg spawning in Plate with the plate spawned in Cabinet....
                if (receptacleSop.PrimaryProperty != SimObjPrimaryProperty.CanPickup) { 
                    if (typesOfObjectsPrefabIsAllowedToSpawnIn.Contains(receptacleSop.ObjType)) {
                        yield return receptacleSop;
                    }
                }
            }
        } else {
            // not found in dictionary!
            #if UNITY_EDITOR
                Debug.Log(simObj.ObjectID +"'s Type is not in the ReceptacleRestrictions dictionary!");
            #endif
        }
    }

    // place each object in the array of objects that should appear in this scene randomly in valid receptacles
    // @seed- random seed used to pick locations
    // @SpawnOnlyOutside - set to true to use only receptacles that are open innately (ie: tables, countertops, sinks) and not ones that require actions to open (drawer, cabinet etc.)
    // @maxPlacementAttempts - the max number of times an object will attempt to be placed in within a receptacle
    // @StaticPlacement - set to true if objects should be placed so they don't roll around after being repositioned
    // @numDuplicatesOfType - used to duplicate the first instance of an object type found in a scene
    // @excludedReceptacles - 
    public bool RandomSpawnRequiredSceneObjects(
        int seed,
        bool spawnOnlyOutside,
        int maxPlacementAttempts,
        bool staticPlacement,
        HashSet<SimObjPhysics> excludedSimObjects,
        ObjectTypeCount[] numDuplicatesOfType,
        List<SimObjType> excludedReceptacleTypes
    ) {
        #if UNITY_EDITOR
            var Masterwatch = System.Diagnostics.Stopwatch.StartNew();
        #endif

        if (SpawnedObjects.Count == 0) {
            throw new InvalidOperationException("There are no spawned objects in the scene!");
        }

        if (RequiredObjects.Count == 0) {
            throw new InvalidOperationException("No objects in Required Objects array, please add them in editor");
        }

        // initialize Unity's random with seed
        UnityEngine.Random.InitState(seed);

        List<SimObjType> TypesOfObjectsPrefabIsAllowedToSpawnIn = new List<SimObjType>();

        // if we already spawned objects, lets just move them around
        int howManyCouldntSpawn = SpawnedObjects.Count;

        Dictionary<SimObjType, List<SimObjPhysics>> typeToObjectList = new Dictionary<SimObjType, List<SimObjPhysics>>();

        Dictionary<SimObjType, int> requestedNumDuplicatesOfType = new Dictionary<SimObjType, int>();
        // List<SimObjType> listOfExcludedReceptacles = new List<SimObjType>();
        HashSet<GameObject> originalObjects = new HashSet<GameObject>(SpawnedObjects);

        if (numDuplicatesOfType == null) {
            numDuplicatesOfType = new ObjectTypeCount[0];
        }

        foreach (ObjectTypeCount repeatCount in numDuplicatesOfType) {
            SimObjType objType = (SimObjType)System.Enum.Parse(typeof(SimObjType), repeatCount.objectType);
            requestedNumDuplicatesOfType[objType] = repeatCount.count;
        }

        // Now lets go through all pickupable sim objects that are in the current scene
        foreach (GameObject go in SpawnedObjects) {
            SimObjPhysics sop = null;
            sop = go.GetComponent<SimObjPhysics>();

            // Add object types in the current scene to the typeToObjectList if not already on it
            if (!typeToObjectList.ContainsKey(sop.ObjType)) {
                typeToObjectList[sop.ObjType] = new List<SimObjPhysics>();
            }

            // Add this sim object to the list if the sim object's type matches the key in typeToObjectList
            if (!requestedNumDuplicatesOfType.ContainsKey(sop.ObjType) ||
                typeToObjectList[sop.ObjType].Count < requestedNumDuplicatesOfType[sop.ObjType]
            ) {
                typeToObjectList[sop.ObjType].Add(sop);
            }
        }

        // Keep track of the sim objects we are making duplicates of
        List<GameObject> gameObjsToPlaceInReceptacles = new List<GameObject>();

        // Keep track of the sim objects that have not been duplicated
        List<GameObject> unduplicatedSimObjects = new List<GameObject>();

        // Ok now lets go through each object type in the dictionary
        foreach (SimObjType sopType in typeToObjectList.Keys) {
            // we found a matching SimObjType and the requested count of duplicates is bigger than how many of that
            // object are currently in the scene
            if (requestedNumDuplicatesOfType.ContainsKey(sopType) &&
                requestedNumDuplicatesOfType[sopType] > typeToObjectList[sopType].Count
            ) {
                foreach (SimObjPhysics sop in typeToObjectList[sopType]) {
                    gameObjsToPlaceInReceptacles.Add(sop.gameObject);
                }

                int numExtra = requestedNumDuplicatesOfType[sopType] - typeToObjectList[sopType].Count;

                // let's instantiate the duplicates now
                for (int j = 0; j < numExtra; j++) {
                    // Add a copy of the item to try and match the requested number of duplicates
                    SimObjPhysics sop = typeToObjectList[sopType][UnityEngine.Random.Range(0, typeToObjectList[sopType].Count - 1)];
                    SimObjPhysics copy = Instantiate(sop);
                    copy.name += "_random_copy_" + j;
                    copy.ObjectID = sop.ObjectID + "_copy_" + j;
                    copy.objectID = copy.ObjectID;
                    gameObjsToPlaceInReceptacles.Add(copy.gameObject);
                }
            } else {
                // this object is not one that needs duplicates, so just add it to the unduplicatedSimObjects list
                foreach (SimObjPhysics sop in typeToObjectList[sopType]) {
                    unduplicatedSimObjects.Add(sop.gameObject);
                }
            }
        }

        System.Random rng = new System.Random(seed);
        gameObjsToPlaceInReceptacles.AddRange(unduplicatedSimObjects);
        gameObjsToPlaceInReceptacles.Shuffle_(rng);

        Dictionary<SimObjType, List<SimObjPhysics>> objTypeToReceptacles = new Dictionary<SimObjType, List<SimObjPhysics>>();
        foreach (SimObjPhysics receptacleSop in GatherAllReceptaclesInScene()) {

            SimObjType receptType = receptacleSop.ObjType;
            if (!excludedReceptacleTypes.Contains(receptacleSop.Type) &&
                    ((!spawnOnlyOutside) || ReceptacleRestrictions.SpawnOnlyOutsideReceptacles.Contains(receptacleSop.ObjType))
            ) {
                if (!objTypeToReceptacles.ContainsKey(receptacleSop.ObjType)) {
                    objTypeToReceptacles[receptacleSop.ObjType] = new List<SimObjPhysics>();
                }
                objTypeToReceptacles[receptacleSop.ObjType].Add(receptacleSop);
            }
        }

        InstantiatePrefabTest spawner = gameObject.GetComponent<InstantiatePrefabTest>();
        foreach (GameObject gameObjToPlaceInReceptacle in gameObjsToPlaceInReceptacles) {
            SimObjPhysics sopToPlaceInReceptacle = gameObjToPlaceInReceptacle.GetComponent<SimObjPhysics>();

            if (excludedSimObjects.Contains(sopToPlaceInReceptacle)) {
                howManyCouldntSpawn--;
                continue;
            }

            bool spawned = false;
            foreach (SimObjPhysics receptacleSop in IterShuffleSimObjPhysicsDictList(objTypeToReceptacles, rng)) {
                List<ReceptacleSpawnPoint> targetReceptacleSpawnPoints;
        
                // check if the target Receptacle is an ObjectSpecificReceptacle
                // if so, if this game object is compatible with the ObjectSpecific restrictions, place it!
                // this is specifically for things like spawning a mug inside a coffee maker
                if (receptacleSop.DoesThisObjectHaveThisSecondaryProperty(SimObjSecondaryProperty.ObjectSpecificReceptacle)) {
                    ObjectSpecificReceptacle osr = receptacleSop.GetComponent<ObjectSpecificReceptacle>();

                    if (osr.HasSpecificType(sopToPlaceInReceptacle.ObjType)) {
                        // in the random spawn function, we need this additional check because there isn't a chance for
                        // the physics update loop to fully update osr.isFull() correctly, which can cause multiple objects
                        // to be placed on the same spot (ie: 2 pots on the same burner)
                        if (osr.attachPoint.transform.childCount > 0) {
                            break;
                        }

                        // perform additional checks if this is a Stove Burner! 
                        if (receptacleSop.GetComponent<SimObjPhysics>().Type == SimObjType.StoveBurner) {
                            if (StoveTopCheckSpawnArea(
                                    sopToPlaceInReceptacle,
                                    osr.attachPoint.transform.position,
                                    osr.attachPoint.transform.rotation,
                                    false
                                )
                            ) {
                                // print("moving object now");
                                gameObjToPlaceInReceptacle.transform.position = osr.attachPoint.position;
                                gameObjToPlaceInReceptacle.transform.SetParent(osr.attachPoint.transform);
                                gameObjToPlaceInReceptacle.transform.localRotation = Quaternion.identity;
                                
                                gameObjToPlaceInReceptacle.GetComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.Discrete;
                                gameObjToPlaceInReceptacle.GetComponent<Rigidbody>().isKinematic = true;

                                howManyCouldntSpawn--;
                                spawned = true;

                                break;
                            }
                        } else {
                            // for everything else (coffee maker, toilet paper holder, etc) just place it if there is nothing attached
                            gameObjToPlaceInReceptacle.transform.position = osr.attachPoint.position;
                            gameObjToPlaceInReceptacle.transform.SetParent(osr.attachPoint.transform);
                            gameObjToPlaceInReceptacle.transform.localRotation = Quaternion.identity;

                            Rigidbody rb = gameObjToPlaceInReceptacle.GetComponent<Rigidbody>();
                            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                            rb.isKinematic = true;
                
                            howManyCouldntSpawn--;
                            spawned = true;
                            break;
                        }
                    }
                }

                targetReceptacleSpawnPoints = receptacleSop.ReturnMySpawnPoints(false);

                // first shuffle the list so it's random
                targetReceptacleSpawnPoints.Shuffle_(rng);
                if (spawner.PlaceObjectReceptacle(
                        rsps: targetReceptacleSpawnPoints,
                        sop: sopToPlaceInReceptacle,
                        placeStationary: staticPlacement,
                        maxPlacementAttempts: maxPlacementAttempts,
                        degreeIncrement: 90,
                        AlwaysPlaceUpright: true // TODO: lowercase on A
                )) {
                    howManyCouldntSpawn--;
                    spawned = true;
                    break;
                } 
            }
        }

        #if UNITY_EDITOR
            Masterwatch.Stop();
            var elapsed = Masterwatch.ElapsedMilliseconds;
            print("total time: " + elapsed);
        #endif

        SetupScene();

        // TODO: this still keeps the configuration..
        // TODO: we should try again for them. It rarely happens, but when it does,
        // it's hard to catch.
        if (howManyCouldntSpawn > 0) {
            throw new InvalidOperationException(gameObjToPlaceInReceptacle.name + " could not be spawned. Consider trying again!");
        }
        return true;
    }


    // a variation of the CheckSpawnArea logic from InstantiatePrefabTest.cs, but filter out things specifically for stove tops
    // which are unique due to being placed close together, which can cause objects placed on them to overlap in super weird ways oh
    // my god it took like 2 days to figure this out it should have been so simple
    public bool StoveTopCheckSpawnArea(SimObjPhysics simObj, Vector3 position, Quaternion rotation, bool spawningInHand) {
        int layermask;

        // first do a check to see if the area is clear

        // if spawning in the agent's hand, ignore collisions with the Agent
        if (spawningInHand) {
            layermask = 1 << 8;
        } else {
            // oh we are spawning it somwhere in the environment, we do need to make sure not to spawn inside the agent or the environment
            layermask = (1 << 8) | (1 << 10);
        }

        // make sure ALL colliders of the simobj are turned off for this check - can't just turn off the Colliders child object because of objects like
        // laptops which have multiple sets of colliders, with one part moving...
        Collider[] objcols = simObj.transform.GetComponentsInChildren<Collider>();

        foreach (Collider col in objcols) {
            if (col.gameObject.name != "BoundingBox") {
                col.enabled = false;
            }
        }

        // keep track of both starting position and rotation to reset the object after performing the check!
        Vector3 originalPos = simObj.transform.position;
        Quaternion originalRot = simObj.transform.rotation;

        // let's move the simObj to the position we are trying, and then change it's rotation to the rotation we are trying
        simObj.transform.position = position;
        simObj.transform.rotation = rotation;

        // now let's get the BoundingBox of the simObj as reference cause we need it to create the overlapbox
        GameObject bb = simObj.BoundingBox.transform.gameObject;
        BoxCollider bbcol = bb.GetComponent<BoxCollider>();

        #if UNITY_EDITOR
            m_Started = true;
            gizmopos = bb.transform.TransformPoint(bbcol.center); 
            // gizmopos = inst.transform.position;
            gizmoscale = bbcol.size;
            // gizmoscale = simObj.BoundingBox.GetComponent<BoxCollider>().size;
            gizmoquaternion = rotation;
        #endif

        // we need the center of the box collider in world space, we need the box collider size/2, we need the rotation to set the box at, layermask, querytrigger
        Collider[] hitColliders = Physics.OverlapBox(bb.transform.TransformPoint(bbcol.center),
                                                     bbcol.size / 2.0f, simObj.transform.rotation, 
                                                     layermask, QueryTriggerInteraction.Ignore);

        // now check if any of the hit colliders were any object EXCEPT other stove top objects i guess
        bool result= true;

        if (hitColliders.Length > 0) {
            foreach (Collider col in hitColliders) {
                // if we hit some structure object like a stove top or countertop mesh, ignore it since we are snapping this to a specific position right here
                if (!col.GetComponentInParent<SimObjPhysics>()) {
                    break;
                }

                // if any sim object is hit that is not a stove burner, then ABORT
                if (col.GetComponentInParent<SimObjPhysics>().Type != SimObjType.StoveBurner) {
                    result = false;
                    simObj.transform.position = originalPos;
                    simObj.transform.rotation = originalRot;

                    foreach (Collider yes in objcols) {
                        if (yes.gameObject.name != "BoundingBox") {
                            yes.enabled = true;
                        }
                    }
                    return result;
                }
            }
        }
         
        // nothing hit in colliders, so we are good to spawn.
        foreach (Collider col in objcols) {
            if (col.gameObject.name != "BoundingBox") {
                col.enabled = true;
            }
        }
        
        simObj.transform.position = originalPos;
        simObj.transform.rotation = originalRot;
        return result; // we are good to spawn, return true
    }

    public List<SimObjPhysics> ShuffleSimObjPhysicsDictList(
        Dictionary<SimObjType, List<SimObjPhysics>> dict,
        int seed
    ) {
        List<SimObjType> types = new List<SimObjType>();
        Dictionary<SimObjType, int> indDict = new Dictionary<SimObjType, int>();
        foreach (KeyValuePair<SimObjType, List<SimObjPhysics>> pair in dict) {
            types.Add(pair.Key);
            indDict[pair.Key] = pair.Value.Count - 1;
        }
        types.Sort();
        types.Shuffle_(seed);
        foreach (SimObjType t in types) {
            dict[t].Shuffle_(seed);
        }

        bool changed = true;
        List<SimObjPhysics> shuffledSopList = new List<SimObjPhysics>();
        while (changed) {
            changed = false;
            foreach (SimObjType type in types) {
                int i = indDict[type];
                if (i >= 0) {
                    changed = true;
                    shuffledSopList.Add(dict[type][i]);
                    indDict[type]--;
                }
            }
        }
        return shuffledSopList;
    }

    public IEnumerable<SimObjPhysics> IterShuffleSimObjPhysicsDictList(
        Dictionary<SimObjType, List<SimObjPhysics>> dict,
        System.Random rng
    ) {
        List<SimObjType> types = new List<SimObjType>();
        Dictionary<SimObjType, int> indDict = new Dictionary<SimObjType, int>();
        foreach (KeyValuePair<SimObjType, List<SimObjPhysics>> pair in dict) {
            types.Add(pair.Key);
            indDict[pair.Key] = pair.Value.Count - 1;
        }
        types.Sort();
        types.Shuffle_(rng);
        foreach (SimObjType t in types) {
            dict[t].Shuffle_(rng);
        }

        bool changed = true;
        List<SimObjPhysics> shuffledSopList = new List<SimObjPhysics>();
        while (changed) {
            changed = false;
            foreach (SimObjType type in types) {
                int i = indDict[type];
                if (i >= 0) {
                    changed = true;
                    yield return dict[type][i];
                    indDict[type]--;
                }
            }
        }
    }

    #if UNITY_EDITOR
        void OnDrawGizmos() {
            Gizmos.color = Color.magenta;
            if (m_Started) {
                Matrix4x4 cubeTransform = Matrix4x4.TRS(gizmopos, gizmoquaternion, gizmoscale);
                Matrix4x4 oldGizmosMatrix = Gizmos.matrix;

                Gizmos.matrix *= cubeTransform;

                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

                Gizmos.matrix = oldGizmosMatrix;
            }

        }
    #endif
}
