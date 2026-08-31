using System.Collections.Generic;
using System.Reflection;
using NetworkSync.GameTime;
using UnityEditor;
using UnityEngine;

namespace NetworkSync.Editor
{
    [CustomPropertyDrawer(typeof(NetworkDeadline))]
    public sealed class NetworkDeadlineDrawer : PropertyDrawer
    {
        private const float LineHeight = 18f;
        private const float InputWidth = 72f;
        private const float LabelFieldGap = 2f;
        private const float GroupSpacing = 8f;

        private static readonly Dictionary<string, EditCache> s_editCache = new();
        private static bool s_updateRegistered;
        private static bool s_repaintRequested;

        private struct EditCache
        {
            public double RemainingSeconds;
            public double EndSeconds;
        }

        static NetworkDeadlineDrawer()
        {
            RegisterEditorUpdate();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!Application.isPlaying)
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }

            return LineHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            NetworkDeadline deadline = GetDeadline(property);
            if (deadline == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            if (!Application.isPlaying)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            RegisterEditorUpdate();

            string cacheKey = GetCacheKey(property);
            if (!s_editCache.ContainsKey(cacheKey))
            {
                SyncCacheFromDeadline(cacheKey, deadline);
            }

            EditCache cache = s_editCache[cacheKey];
            bool canEdit = deadline.CanLocalClientWrite();
            bool appliedEdit = false;

            Rect lineRect = new(position.x, position.y, position.width, LineHeight);
            lineRect = EditorGUI.PrefixLabel(lineRect, GUIUtility.GetControlID(FocusType.Passive), label);

            float x = lineRect.x;

            GUIContent remainingLabel = new GUIContent("Remaining (s)", "Remaining seconds");
            float remainingLabelWidth = EditorStyles.miniLabel.CalcSize(remainingLabel).x;
            EditorGUI.LabelField(new Rect(x, lineRect.y, remainingLabelWidth, LineHeight), remainingLabel, EditorStyles.miniLabel);
            x += remainingLabelWidth + LabelFieldGap;
            Rect remainingRect = new(x, lineRect.y, InputWidth, LineHeight);
            x += InputWidth + GroupSpacing;

            GUIContent endLabel = new GUIContent("End (s)", "Deadline on NetworkGameTime axis");
            float endLabelWidth = EditorStyles.miniLabel.CalcSize(endLabel).x;
            EditorGUI.LabelField(new Rect(x, lineRect.y, endLabelWidth, LineHeight), endLabel, EditorStyles.miniLabel);
            x += endLabelWidth + LabelFieldGap;
            Rect endRect = new(x, lineRect.y, InputWidth, LineHeight);

            EditorGUI.BeginDisabledGroup(!canEdit);

            EditorGUI.BeginChangeCheck();
            cache.RemainingSeconds = EditorGUI.DoubleField(remainingRect, GUIContent.none, cache.RemainingSeconds);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyRemainingEdit(deadline, ref cache);
                s_editCache[cacheKey] = cache;
                appliedEdit = true;
            }

            EditorGUI.BeginChangeCheck();
            cache.EndSeconds = EditorGUI.DoubleField(endRect, GUIContent.none, cache.EndSeconds);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyEndEdit(deadline, ref cache);
                s_editCache[cacheKey] = cache;
                appliedEdit = true;
            }

            EditorGUI.EndDisabledGroup();

            if (!appliedEdit)
            {
                SyncCacheFromDeadline(cacheKey, deadline);
            }

            if (deadline.IsActive)
            {
                s_repaintRequested = true;
            }
        }

        private static void ApplyRemainingEdit(NetworkDeadline deadline, ref EditCache cache)
        {
            if (cache.RemainingSeconds < 0d)
            {
                cache.RemainingSeconds = 0d;
            }

            if (!deadline.IsActive)
            {
                if (cache.RemainingSeconds > 0d)
                {
                    deadline.SetFromDuration(cache.RemainingSeconds);
                }

                SyncCacheFromDeadlineAfterEdit(deadline, ref cache);
                return;
            }

            deadline.Value = NetworkGameTime.Seconds + cache.RemainingSeconds;
            SyncCacheFromDeadlineAfterEdit(deadline, ref cache);
        }

        private static void ApplyEndEdit(NetworkDeadline deadline, ref EditCache cache)
        {
            if (cache.EndSeconds < 0d)
            {
                cache.EndSeconds = 0d;
            }

            deadline.Value = cache.EndSeconds;
            SyncCacheFromDeadlineAfterEdit(deadline, ref cache);
        }

        private static void SyncCacheFromDeadlineAfterEdit(NetworkDeadline deadline, ref EditCache cache)
        {
            cache.RemainingSeconds = deadline.RemainingSeconds;
            cache.EndSeconds = deadline.Value;
        }

        private static void RegisterEditorUpdate()
        {
            if (s_updateRegistered)
            {
                return;
            }

            s_updateRegistered = true;
            EditorApplication.update += HandleEditorUpdate;
        }

        private static void HandleEditorUpdate()
        {
            if (!Application.isPlaying || !s_repaintRequested)
            {
                return;
            }

            s_repaintRequested = false;

            ActiveEditorTracker tracker = ActiveEditorTracker.sharedTracker;
            for (int i = 0; i < tracker.activeEditors.Length; i++)
            {
                tracker.activeEditors[i]?.Repaint();
            }
        }

        private static void SyncCacheFromDeadline(string cacheKey, NetworkDeadline deadline)
        {
            s_editCache[cacheKey] = new EditCache
            {
                RemainingSeconds = deadline.RemainingSeconds,
                EndSeconds = deadline.Value
            };
        }

        private static string GetCacheKey(SerializedProperty property)
        {
            return property.serializedObject.targetObject.GetEntityId() + property.propertyPath;
        }

        private static NetworkDeadline GetDeadline(SerializedProperty property)
        {
            object target = property.serializedObject.targetObject;
            if (target == null)
            {
                return null;
            }

            string path = property.propertyPath;
            if (path.Contains("."))
            {
                return GetNestedDeadline(target, path);
            }

            FieldInfo field = target.GetType().GetField(
                path,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            return field?.GetValue(target) as NetworkDeadline;
        }

        private static NetworkDeadline GetNestedDeadline(object root, string path)
        {
            string[] parts = path.Split('.');
            object current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                if (current == null)
                {
                    return null;
                }

                FieldInfo field = current.GetType().GetField(
                    parts[i],
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (field == null)
                {
                    return null;
                }

                current = field.GetValue(current);
            }

            return current as NetworkDeadline;
        }
    }
}
