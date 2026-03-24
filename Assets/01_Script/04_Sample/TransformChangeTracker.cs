using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

    public class TransformChangeTracker : MonoBehaviour
    {
        Vector3 lastPos;
        Quaternion lastRot;

        void Start()
        {
            lastPos = transform.position;
            lastRot = transform.rotation;
        }

        void Update()
        {
            if (transform.position != lastPos || transform.rotation != lastRot)
            {
                StackTrace stack = new StackTrace(true);
                StackFrame[] frames = stack.GetFrames();

                StringBuilder userStack = new StringBuilder();

                if (frames != null)
                {
                    foreach (var frame in frames)
                    {
                        var method = frame.GetMethod();
                        if (method == null) continue;

                        var type = method.DeclaringType;
                        if (type == null) continue;

                        string ns = type.Namespace ?? "";

                        // UnityEngine 제외 = 사용자 코드만
                        if (!ns.StartsWith("UnityEngine"))
                        {
                            userStack.AppendLine(
                                $"{type.FullName}.{method.Name} " +
                                $"({frame.GetFileName()}:{frame.GetFileLineNumber()})"
                            );
                        }
                    }
                }

                UnityEngine.Debug.Log(
                    $"[Transform Changed]\n" +
                    $"Object : {name}\n" +
                    $"Position : {lastPos} -> {transform.position}\n" +
                    $"Rotation : {lastRot.eulerAngles} -> {transform.rotation.eulerAngles}\n\n" +
                    $"User CallStack:\n{userStack}"
                );

                lastPos = transform.position;
                lastRot = transform.rotation;
            }
        }
    }