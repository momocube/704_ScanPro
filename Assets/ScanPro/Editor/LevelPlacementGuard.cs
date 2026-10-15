using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ScanPro704.Editor
{
    public static class LevelPlacementGuard
    {
        public const string Rejection = "入口位置固定，禁止放置任何物件或穿越入口的路徑；本次修改已取消。";
        public const string ScoreboardRejection = "東小牆整面保留為記分板，禁止放置物件或讓移動路徑穿越；本次修改已取消。";
        public const string SafeGreenFloorRejection = "安全區只能放在地板（Floor／face 2），完整移動路徑也必須留在地板範圍內；本次修改已取消。";
        public const string BlackAreaRejection = "物件與完整循環路徑必須留在可放置區域；本次修改已取消。";
        public static bool Apply(SerializedObject serialized,out string message)
        {
            message=null;
            if(!serialized.hasModifiedProperties) return false;
            var venue=serialized.FindProperty("venue").objectReferenceValue as VenueLayout;
            var items=serialized.FindProperty("items");
            for(var i=0;i<items.arraySize;i++)
            {
                var property=items.GetArrayElementAtIndex(i);
                var item=new LevelItem {kind=(TileKind)property.FindPropertyRelative("kind").enumValueIndex,
                    shape=(TileShape)property.FindPropertyRelative("shape").enumValueIndex,
                    position=property.FindPropertyRelative("position").vector2IntValue,size=property.FindPropertyRelative("size").vector2IntValue,
                    allowSafeGreenOutsideFloor=property.FindPropertyRelative("allowSafeGreenOutsideFloor").boolValue};
                var frames=property.FindPropertyRelative("animationFrames");
                for(var j=0;j<frames.arraySize;j++)
                {
                    var frame=frames.GetArrayElementAtIndex(j);
                    item.animationFrames.Add(new LevelMaskFrame
                    {
                        offset=frame.FindPropertyRelative("offset").vector2IntValue,
                        width=frame.FindPropertyRelative("width").intValue,
                        height=frame.FindPropertyRelative("height").intValue,
                        packedPixels=frame.FindPropertyRelative("packedPixels").stringValue
                    });
                }
                if(item.IsScan)
                {
                    var qrErrors=LevelValidation.ContentErrors(item);
                    if(qrErrors.Count>0)
                    {serialized.Update();message=qrErrors[0]+"；本次修改已取消。";return false;}
                }
                if(item.shape!=TileShape.Rectangle && !TileShapeUtility.HasSquareBounds(item))
                {serialized.Update();message="方形與圓形物件需使用相同寬高；本次修改已取消。";return false;}
                var path=property.FindPropertyRelative("waypoints");
                for(var j=0;j<path.arraySize;j++) item.waypoints.Add(path.GetArrayElementAtIndex(j).vector2IntValue);
                var unit=venue==null ? 10 : venue.pixelsPerCell;
                if(item.kind==TileKind.SafeGreen && !item.allowSafeGreenOutsideFloor && !VenueLayout.PathIsOnFloor(item,unit))
                {serialized.Update();message=SafeGreenFloorRejection;return false;}
                if(VenueLayout.TouchesEntrance(item,unit))
                {serialized.Update();message=Rejection;return false;}
                if(VenueLayout.TouchesScoreboard(item,unit))
                {serialized.Update();message=ScoreboardRejection;return false;}
                if(venue!=null && !venue.PathIsAllowed(item))
                {serialized.Update();message=BlackAreaRejection;return false;}
            }
            return serialized.ApplyModifiedProperties();
        }
        public static bool ApplyProfile(SerializedObject serialized,out string message)
        {
            message=null;
            if(!serialized.hasModifiedProperties) return false;
            var mappings=new List<SurfaceMapping>();var surfaces=serialized.FindProperty("surfaces");
            for(var i=0;i<surfaces.arraySize;i++)
            {
                var property=surfaces.GetArrayElementAtIndex(i);
                mappings.Add(new SurfaceMapping {faceIndex=property.FindPropertyRelative("faceIndex").intValue,
                    sourcePixels=property.FindPropertyRelative("sourcePixels").rectIntValue,outputPixels=property.FindPropertyRelative("outputPixels").rectIntValue,
                    rotation=(SurfaceRotation)property.FindPropertyRelative("rotation").enumValueIndex,name=property.FindPropertyRelative("name").stringValue});
            }
            if(!OutputProfile.HasFixedEntrance(mappings,out message) || !OutputProfile.HasFixedScoreboard(mappings,out message)){serialized.Update();return false;}
            return serialized.ApplyModifiedProperties();
        }
    }
    [CustomEditor(typeof(ScanLevel))]
    public sealed class ScanLevelInspector : UnityEditor.Editor
    {
        string message;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (!ScanProWorkbench.CanEditAsset(target as ScanLevel))
            {
                EditorGUILayout.HelpBox("只有目前五關與草稿可直接編輯。此關卡維持唯讀；請從 704 關卡工作台建立草稿副本。", MessageType.Warning);
                using (new EditorGUI.DisabledScope(true)) DrawPropertiesExcluding(serializedObject,"m_Script","layerOrder");
                return;
            }
            DrawPropertiesExcluding(serializedObject,"m_Script","layerOrder");
            if(serializedObject.hasModifiedProperties) LevelPlacementGuard.Apply(serializedObject,out message);
            EditorGUILayout.HelpBox(message ?? "固定入口與東小牆記分板為禁止區；物件及完整移動路徑都必須避開。",message==null ? MessageType.Info : MessageType.Warning);
        }
    }
    [CustomEditor(typeof(OutputProfile))]
    public sealed class FixedOutputProfileInspector : UnityEditor.Editor
    {
        string message;
        public override void OnInspectorGUI()
        {
            var profile=(OutputProfile)target;
            if(GUILayout.Button("套用 704 固定場域配置"))
            {Undo.RecordObject(profile,"704 固定場域配置");profile.Configure704();EditorUtility.SetDirty(profile);message=null;}
            serializedObject.Update();DrawPropertiesExcluding(serializedObject,"m_Script");
            if(serializedObject.hasModifiedProperties) LevelPlacementGuard.ApplyProfile(serializedObject,out message);
            var displays = new List<DisplayInfo>(); Screen.GetDisplayLayout(displays);
            var displayNames = new List<string>();
            for (var i = 0; i < displays.Count; i++) displayNames.Add($"{i}: {displays[i].name}  {displays[i].width}×{displays[i].height}");
            EditorGUILayout.HelpBox("全螢幕使用指定顯示器的原生解析度，完整等比例置中，剩餘區域留黑；704 正式顯示端應設定為 2688×3840。\n本機顯示器：\n" + string.Join("\n", displayNames), MessageType.Info);
            EditorGUILayout.HelpBox(message ?? "入口與東小牆記分板面為全關卡固定配置，不可移動、刪除或改變大小。",message==null ? MessageType.Info : MessageType.Warning);
        }
    }
}
