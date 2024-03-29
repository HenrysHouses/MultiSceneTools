// *   Multi Scene Tools For Unity
// *
// *   Copyright (C) 2023 Henrik Hustoft
// *
// *   Licensed under the Apache License, Version 2.0 (the "License");
// *   you may not use this file except in compliance with the License.
// *   You may obtain a copy of the License at
// *
// *       http://www.apache.org/licenses/LICENSE-2.0
// *
// *   Unless required by applicable law or agreed to in writing, software
// *   distributed under the License is distributed on an "AS IS" BASIS,
// *   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// *   See the License for the specific language governing permissions and
// *   limitations under the License.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using System.Threading.Tasks;
using System.Threading;

namespace HH.MultiSceneTools
{
    public enum collectionLoadMode
    {
        Difference,
        Replace,
        Additive
    }

    public static class MultiSceneLoader
    {
        public static UnityEvent<SceneCollection, collectionLoadMode> OnSceneCollectionLoaded = new UnityEvent<SceneCollection, collectionLoadMode>();
        public static UnityEvent<SceneCollection, collectionLoadMode> OnSceneCollectionLoadDebug = new UnityEvent<SceneCollection, collectionLoadMode>();
        public static int getDebugEventCount {get; private set;}
        private static bool IsLoggingOnSceneLoad;
        private static Scene loadedBootScene;
        public static SceneCollection currentlyLoaded {private set; get;}
        public static string getLoadedCollectionTitle => currentlyLoaded.Title;

        #if UNITY_EDITOR
            public static SceneCollection setCurrentlyLoaded(SceneCollection collection) => currentlyLoaded = collection;
        #endif

        public static void loadCollection(string CollectionTitle, collectionLoadMode mode)
        {
            if(currentlyLoaded == null)
            {
                currentlyLoaded = ScriptableObject.CreateInstance<SceneCollection>(); 
                currentlyLoaded.name = "None";
            }

            if(MultiSceneToolsConfig.instance.LogOnSceneChange)
                AddLogOnLoad();

            SceneCollection TargetCollection = null;

            foreach (SceneCollection target in MultiSceneToolsConfig.instance.GetSceneCollections())
            {
                if(target.Title.Equals(CollectionTitle))
                {
                    TargetCollection = target;
                    break;
                }
            }

            if(TargetCollection == null)
            {
                Debug.LogError("Could not find Scene Collection of name: " + CollectionTitle);
                return;
            }

            if(TargetCollection.SceneNames.Count == 0)
            {
                Debug.LogWarning("Attempted to load a scene collection that contains no scenes", TargetCollection);
                return;
            }

            switch(mode)
            {
                case collectionLoadMode.Difference:
                    loadDifference(TargetCollection);
                    break;

                case collectionLoadMode.Replace:
                    loadReplace(TargetCollection);
                    break;

                case collectionLoadMode.Additive:
                    loadAdditive(TargetCollection);
                    break;
            }
            OnSceneCollectionLoadDebug?.Invoke(TargetCollection, mode);
            OnSceneCollectionLoaded?.Invoke(TargetCollection, mode);

            setActiveScene(TargetCollection).ContinueWith(task => {Debug.Log("Set Active Scene: " + TargetCollection.GetNameOfTargetActiveScene());});

            #if UNITY_EDITOR
            MultiSceneToolsConfig.instance.setCurrCollection(currentlyLoaded);
            #endif
        }

        public static void loadCollection(SceneCollection Collection, collectionLoadMode mode)
        {
            if(currentlyLoaded == null)
            {
                currentlyLoaded = ScriptableObject.CreateInstance<SceneCollection>(); 
                currentlyLoaded.name = "None";
            }

            if(MultiSceneToolsConfig.instance.LogOnSceneChange)
                AddLogOnLoad();

            if(Collection == null)
            {
                throw new System.NullReferenceException();
            }

            if(Collection.SceneNames.Count == 0)
            {
                Debug.LogWarning("Attempted to load a scene collection that contains no scenes", Collection);
                return;
            }

            switch(mode)
            {
                case collectionLoadMode.Difference:
                    loadDifference(Collection);
                    break;

                case collectionLoadMode.Replace:
                    loadReplace(Collection);
                    break;

                case collectionLoadMode.Additive:
                    loadAdditive(Collection);
                    break;
            }
            OnSceneCollectionLoadDebug?.Invoke(Collection, mode);
            OnSceneCollectionLoaded?.Invoke(Collection, mode);
            
            setActiveScene(Collection).ContinueWith(task => {Debug.Log("Set Active Scene: " + Collection.GetNameOfTargetActiveScene());});

            #if UNITY_EDITOR
            MultiSceneToolsConfig.instance.setCurrCollection(currentlyLoaded);
            #endif
        }

        static void loadDifference(SceneCollection Collection)
        {
            if(currentlyLoaded == null)
            {
                throw new UnityException("No currently loaded scene collection.");
            }

            string bootScene = getBootSceneName();
            bool shouldKeepBoot = false;
            bool shouldReplaceScene = false;
            
            if(loadedBootScene.name != null)
                shouldKeepBoot = true;

            if(currentlyLoaded.SceneNames.Contains(bootScene) && MultiSceneToolsConfig.instance.UseBootScene)
            {
                shouldKeepBoot = true;
                loadedBootScene = MultiSceneToolsConfig.instance.BootScene;
            }

            // Unload Differences
            int unloadedScenes = 0;
            for (int i = 0; i < currentlyLoaded.SceneNames.Count; i++)
            {
                bool difference = true;
                foreach (string targetScene in Collection.SceneNames)
                {
                    if(currentlyLoaded.SceneNames[i].Equals(targetScene))
                    {
                        difference = false;
                    }
                }
                if(!difference)
                    continue;
                
                if(currentlyLoaded.SceneNames[i] == bootScene && shouldKeepBoot)
                    continue;

                if(unloadedScenes != currentlyLoaded.SceneNames.Count-1 || loadedBootScene.name != null)
                {
                    unloadedScenes++;
                    unload(currentlyLoaded.SceneNames[i]);
                }
                else
                {
                    if(!shouldKeepBoot)
                        shouldReplaceScene = true;
                    break;
                }
            }
            // load Differences
            foreach (string targetScene in Collection.SceneNames)
            {
                bool difference = true;
                foreach (string LoadedScene in currentlyLoaded.SceneNames)
                {
                    if(targetScene.Equals(bootScene) && loadedBootScene.name != null)
                        difference = false;
                    
                    if(targetScene.Equals(LoadedScene))
                    {
                        difference = false;
                    }
                }
                if(difference)
                {
                    if(shouldReplaceScene)
                        load(targetScene, LoadSceneMode.Single);
                    else
                        load(targetScene, LoadSceneMode.Additive);
                }
            }
            currentlyLoaded = Collection;
        }

        static void loadReplace(SceneCollection Collection)
        {
            bool loadBoot = MultiSceneToolsConfig.instance.UseBootScene;
            string bootScene = getBootSceneName();
            bool shouldKeepBoot = false;
            bool shouldReplaceScene = false;

            if(loadedBootScene.name != null)
                shouldKeepBoot = true;

            if(currentlyLoaded.SceneNames.Contains(bootScene) && loadBoot)
            {
                shouldKeepBoot = true;
                loadedBootScene = MultiSceneToolsConfig.instance.BootScene;
            }

            if(loadBoot && loadedBootScene.name == null)
                shouldReplaceScene = true;

            // Unload Scenes
            int unloadedScenes = 0;
            for (int i = 0; i < currentlyLoaded.SceneNames.Count; i++)
            {
                if(shouldReplaceScene)
                    break;

                if(currentlyLoaded.SceneNames.Count < 2 && !loadBoot)
                {
                    shouldReplaceScene = true;
                    continue;
                }

                if(currentlyLoaded.SceneNames[i].Equals(bootScene) && loadedBootScene.name != null)
                    continue;

                if(unloadedScenes != currentlyLoaded.SceneNames.Count-1 || loadedBootScene.name != null)
                {
                    unloadedScenes++;
                    unload(currentlyLoaded.SceneNames[i]);
                }
                else
                {
                    if(!shouldKeepBoot)
                        shouldReplaceScene = true;
                    break;
                }
            }

            for (int i = 0; i < Collection.SceneNames.Count; i++)
            {
                if(loadBoot)
                {
                    if(Collection.SceneNames[i] == bootScene)
                        continue;

                    if(shouldReplaceScene)
                    {
                        load(Collection.SceneNames[i], LoadSceneMode.Single);
                        shouldReplaceScene = false;
                    }
                    else
                        load(Collection.SceneNames[i], LoadSceneMode.Additive);
                }
                else if(i == 0)
                    load(Collection.SceneNames[i], LoadSceneMode.Single);
                else
                    load(Collection.SceneNames[i], LoadSceneMode.Additive);
            }
            currentlyLoaded = Collection;
        }


        static void loadAdditive(SceneCollection Collection)
        {
            throw new System.NotImplementedException();
            
            // for (int i = 0; i < Collection.SceneNames.Count; i++)
            // {
            //     load(Collection.SceneNames[i], LoadSceneMode.Additive);
            // }
            // MultiSceneToolsConfig.instance.setCurrCollection(currentlyLoaded);
        }

        static SceneCollection FindCollection(string CollectionTitle)
        {
            foreach (SceneCollection target in MultiSceneToolsConfig.instance.GetSceneCollections())
            {
                if(target.Title == CollectionTitle)
                    return target;
            }
            Debug.LogWarning("Could not find collection");
            return null;
        }

        static async Task setActiveScene(SceneCollection collection)
        {
            if(collection.ActiveSceneIndex < 0)
                return;

            Scene targetActive = new Scene();
            
            while(!targetActive.isLoaded)
            {
                targetActive = SceneManager.GetSceneByName(collection.GetNameOfTargetActiveScene());
                await Task.Yield();
            }

            SceneManager.SetActiveScene(targetActive);
        }

        static string getBootSceneName()
        {
            return MultiSceneToolsConfig.instance.BootScene.name;
        }

        static void unload(string SceneName)
        {
            SceneManager.UnloadSceneAsync(SceneName);
        }

        static void load(string SceneName, LoadSceneMode mode)
        {
            SceneManager.LoadScene(SceneName, mode);
        }

        // * --- Debugging --- 
        private static void logSceneChange(SceneCollection collection, collectionLoadMode mode)
        {
            Debug.Log("Loaded: \"" + collection.Title + "\" in mode: " + mode.ToString());
        } 

        private static void AddLogOnLoad()
        {
            if(IsLoggingOnSceneLoad)
                return;

            OnSceneCollectionLoadDebug.AddListener(logSceneChange);
            IsLoggingOnSceneLoad = true;
        }
    }
}