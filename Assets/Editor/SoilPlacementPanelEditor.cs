#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class SoilPlacementPanelEditor
{
    [MenuItem("Tools/Tree Planting/Create or Update Placement Button Panel")]
    private static void Create()
    {
        SoilPatchPlacement placement = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<SoilPatchPlacement>() : null;
        if (placement == null)
        { EditorUtility.DisplayDialog("Soil placement", "Select the object with SoilPatchPlacement first.", "OK"); return; }
        SerializedObject serialized = new SerializedObject(placement);
        GameObject panel = serialized.FindProperty("placementControls").objectReferenceValue as GameObject;
        bool created = panel == null;
        if (created)
        {
            panel = new GameObject("Soil Placement Controls", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, "Create soil placement controls");
        }
        else
        {
            // Keep Meta canvas surfaces, interactables and other setup children.
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = panel.transform.GetChild(i).gameObject;
                if (child.GetComponent<Button>() != null || child.GetComponent<Text>() != null)
                    Undo.DestroyObjectImmediate(child);
            }
        }
        Canvas canvas = panel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        RectTransform rect = (RectTransform)panel.transform;
        rect.sizeDelta = new Vector2(720f, 180f);
        rect.localScale = Vector3.one * 0.001f;
        if (created) rect.position = placement.transform.position + Vector3.forward * 1f + Vector3.up * 1.1f;
        panel.GetComponent<Image>().color = new Color(0.04f,0.07f,0.09f,0.95f);
        string[] labels = { "Rotate Left", "Rotate Right", "Reset", "Start Game" };
        UnityAction[] actions = { placement.RotateLeft, placement.RotateRight,
            placement.ResetPlacement, placement.ConfirmPlacement };
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i=0;i<labels.Length;i++)
        {
            GameObject obj = new GameObject(labels[i], typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(panel.transform,false);
            RectTransform buttonRect = (RectTransform)obj.transform;
            buttonRect.sizeDelta = new Vector2(158f,100f);
            buttonRect.anchoredPosition = new Vector2((i-1.5f)*170f,-15f);
            obj.GetComponent<Image>().color = i==3 ? new Color(0.1f,0.5f,0.3f) : new Color(0.12f,0.22f,0.28f);
            Button button = obj.GetComponent<Button>();
            button.targetGraphic = obj.GetComponent<Image>();
            UnityEventTools.AddPersistentListener(button.onClick, actions[i]);
            GameObject textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(obj.transform,false);
            RectTransform textRect=(RectTransform)textObj.transform;
            textRect.anchorMin=Vector2.zero; textRect.anchorMax=Vector2.one;
            textRect.offsetMin=Vector2.zero; textRect.offsetMax=Vector2.zero;
            Text text=textObj.GetComponent<Text>();
            text.text=labels[i];text.font=font;text.fontSize=20;
            text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
        }
        GameObject titleObj=new GameObject("Title",typeof(RectTransform),typeof(Text));
        titleObj.transform.SetParent(panel.transform,false);
        RectTransform titleRect=(RectTransform)titleObj.transform;
        titleRect.sizeDelta=new Vector2(680,40);titleRect.anchoredPosition=new Vector2(0,60);
        Text title=titleObj.GetComponent<Text>();title.text="Pinch the patch to move. Release to place.";
        title.font=font;title.fontSize=25;title.color=Color.white;
        title.alignment=TextAnchor.MiddleCenter;title.raycastTarget=false;
        serialized.Update();
        serialized.FindProperty("placementControls").objectReferenceValue=panel;
        serialized.ApplyModifiedProperties();
        Selection.activeGameObject=panel;
    }
}
#endif
