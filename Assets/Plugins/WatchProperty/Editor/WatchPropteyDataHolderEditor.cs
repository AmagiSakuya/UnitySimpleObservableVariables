using UnityEditor;

namespace AmagiSakuya.ObservableVariables.ObservableVariablesEditor
{
    [CustomEditor(typeof(WatchPropertyDataHolder), true)]
    public class WatchPropertyDataHolderEditor : Editor
    {
        WatchPropertyDataHolder t;
        void OnEnable()
        {
            t = (WatchPropertyDataHolder)target;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
        }
    }
}