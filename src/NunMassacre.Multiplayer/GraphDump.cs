using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using NodeCanvas.Framework;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer
{
    // Research aid for the diagnostics file: the game's gameplay flow lives in NodeCanvas graphs and
    // serialized UnityEvents, which the decompiled C# does not show.
    internal static class GraphDump
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void Write(StringBuilder report)
        {
            foreach (var owner in Resources.FindObjectsOfTypeAll<GraphOwner>().Where(o => o && o.gameObject.scene.IsValid()))
            {
                var graph = owner.graph;
                report.AppendLine("GRAPH " + Path(owner.transform) + " | "
                    + (graph ? graph.GetType().Name + " " + graph.name : "none") + " | running=" + owner.isRunning);
                if (!graph) continue;
                foreach (var node in graph.allNodes)
                {
                    report.AppendLine("  NODE " + Safe(node.name) + (string.IsNullOrEmpty(node.tag) ? "" : " #" + node.tag)
                        + " <" + node.GetType().Name + ">" + (node == graph.primeNode ? " START" : ""));
                    var container = node as ISubTasksContainer;
                    if (container != null) foreach (var sub in container.GetSubTasks()) WriteTask(report, "      ", sub);
                    else WriteTask(report, "      ", Task(node));
                    var nested = node.GetType().GetProperty("subGraph", Fields);
                    if (nested != null) { var sub = nested.GetValue(node, null) as Graph; if (sub) report.AppendLine("      SUBGRAPH " + sub.name); }
                    foreach (var connection in node.outConnections)
                    {
                        report.AppendLine("    -> " + Safe(connection.targetNode == null ? "?" : connection.targetNode.name));
                        WriteTask(report, "         ", Task(connection));
                    }
                }
            }
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(m => m && m.gameObject.scene.IsValid()))
            {
                for (Type type = behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                {
                    foreach (var field in type.GetFields(Fields | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(UnityEventBase).IsAssignableFrom(field.FieldType)) continue;
                        var value = field.GetValue(behaviour) as UnityEventBase;
                        if (value == null || value.GetPersistentEventCount() == 0) continue;
                        report.Append("EVENT " + Path(behaviour.transform) + " | " + behaviour.GetType().Name + "." + field.Name + " :");
                        for (int i = 0; i < value.GetPersistentEventCount(); i++)
                        {
                            var target = value.GetPersistentTarget(i);
                            report.Append(" ["
                                + (target ? (target is Component ? Path(((Component)target).transform) : target.name) + "/" + target.GetType().Name : "null")
                                + "." + value.GetPersistentMethodName(i) + "]");
                        }
                        report.AppendLine();
                    }
                }
            }
        }
        private static object Task(object owner)
        {
            var property = owner.GetType().GetProperty("task", Fields);
            return property == null ? null : property.GetValue(owner, null);
        }
        private static void WriteTask(StringBuilder report, string indent, object task)
        {
            var value = task as NodeCanvas.Framework.Task;
            if (value == null) return;
            var actions = value as ActionList;
            var conditions = value as ConditionList;
            if (actions != null) { foreach (var child in actions.actions) WriteTask(report, indent, child); return; }
            if (conditions != null)
            {
                report.AppendLine(indent + conditions.checkMode);
                foreach (var child in conditions.conditions) WriteTask(report, indent + "  ", child);
                return;
            }
            report.AppendLine(indent + value.GetType().FullName + ": " + Safe(value.summaryInfo));
        }
        private static string Safe(string text) { return (text ?? "").Replace("\n", " ").Replace("\r", " "); }
        internal static string Path(Transform t)
        {
            var builder = new StringBuilder(t ? t.name : "null");
            for (var p = t ? t.parent : null; p; p = p.parent) builder.Insert(0, p.name + "/");
            return builder.ToString();
        }
    }
}
