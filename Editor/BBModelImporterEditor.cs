using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace BBImporter
{
    [CustomEditor(typeof(BBModelImporter))]
    [CanEditMultipleObjects]
    public class BBModelImporterEditor : ScriptedImporterEditor
    {
        private SerializedProperty m_materialTemplate;
        private SerializedProperty m_importMode;
        private SerializedProperty m_filterHidden;
        private SerializedProperty m_ignoreName;

        public override void OnEnable()
        {
            base.OnEnable();
            // 在OnEnable中检索serializedObject属性并存储
            m_materialTemplate = serializedObject.FindProperty("materialTemplate");
            m_importMode = serializedObject.FindProperty("importMode");
            m_filterHidden = serializedObject.FindProperty("filterHidden");
            m_ignoreName = serializedObject.FindProperty("ignoreName");
        }
    
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
        
            // 使用中文标签显示属性字段
            EditorGUILayout.PropertyField(m_materialTemplate,
                new GUIContent("材质模板", "用于模型渲染的材质模板。如果未指定，将使用默认材质"));
            EditorGUILayout.PropertyField(m_importMode,
                new GUIContent("导入模式", "选择导入模式：\n• 合并对象 - 将所有几何体合并为单个对象\n• 分离对象 - 每个元素创建独立的游戏对象\n• 带层级和动画 - 保持原始层级结构并支持动画"));
            EditorGUILayout.PropertyField(m_filterHidden,
                new GUIContent("过滤隐藏对象", "是否在导入时忽略在Blockbench中被标记为隐藏的对象"));
            EditorGUILayout.PropertyField(m_ignoreName,
                new GUIContent("忽略名称", "在导入时忽略的对象名称列表，支持通配符匹配"));

            // 应用更改以便撤销/重做功能正常工作
            // 报错提示ApplyModifiedProperties和ApplyRevertGUI重复了，故注释该行。
            // serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("重新导入"))
            {
#if UNITY_2022_3_OR_NEWER
                SaveChanges();
#else
                ApplyAndImport();
#endif
            }

            // 调用ApplyRevertGUI显示应用和还原按钮
            ApplyRevertGUI();
        }
    }
}