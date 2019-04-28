using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;

namespace DebuggingTools
{
    class Program
    {
        private static TaskStatus GetTaskStatus(int stateFlags) => (stateFlags & 2097152) == 0
            ? ((stateFlags & 4194304) == 0
                ? ((stateFlags & 16777216) == 0
                    ? ((stateFlags & 8388608) == 0
                        ? ((stateFlags & 131072) == 0
                            ? ((stateFlags & 65536) == 0
                                ? ((stateFlags & 33554432) == 0
                                    ? TaskStatus.Created
                                    : TaskStatus.WaitingForActivation)
                                : TaskStatus.WaitingToRun)
                            : TaskStatus.Running)
                        : TaskStatus.WaitingForChildrenToComplete)
                    : TaskStatus.RanToCompletion)
                : TaskStatus.Canceled)
            : TaskStatus.Faulted;
        
        static void Main(string[] args)
        {
            if (!File.Exists(args[0]))
            {
                Console.WriteLine("Please provide path to memory dump");
                return;
            }

            var tasksObjests = new HashSet<ulong>();
            using (var dataTarget = DataTarget.LoadCrashDump(args[0]))
            {
                ClrInfo runtimeInfo = dataTarget.ClrVersions[0];  // just using the first runtime
                ClrRuntime runtime = runtimeInfo.CreateRuntime();
                foreach (ulong obj in runtime.Heap.EnumerateObjectAddresses())
                {
                    ClrType type = runtime.Heap.GetObjectType(obj);

                    if (type.Name.StartsWith(typeof(Task).FullName))
                    {
                        var flagsValue = type.GetFieldByName("m_stateFlags")?.GetValue(obj);
                        if (flagsValue != null && flagsValue is int stateFlags)
                        {
                            var status = GetTaskStatus(stateFlags);
                            Console.WriteLine($"Found {type.Name}, state {status}, memory address {obj}");
                            tasksObjests.Add(obj);
                        }
                    }
                }

                Console.WriteLine("\nEnter memory address to explore GC roots or enter to end\n");

                while (true)
                {
                    if (ulong.TryParse(Console.ReadLine(), out var address) && tasksObjests.Contains(address))
                    {
                        Console.WriteLine();
                        ShowMemoryRoots(runtime.Heap, address);
                    }
                    else
                    {
                        return;
                    }
                }
            }

        }

        private static void ShowMemoryRoots(ClrHeap heap, ulong address)
        {
            foreach (var root in heap.EnumerateRoots())
            {
                FindTrace(heap, root.Object, address, new HashSet<ulong>(), new Stack<string>());
            }
        }

        private static void FindTrace(ClrHeap heap, ulong source, ulong target, HashSet<ulong> visited, Stack<string> stack)
        {
            if (visited.Contains(source))
            {
                return;
            }

            visited.Add(source);
            var type = heap.GetObjectType(source);
            if (type != null)
            {
                stack.Push(type.Name);
                type.EnumerateRefsOfObject(source, (innerObject, fieldOffset) =>
                {
                    if (innerObject == target)
                    {
                        var targetType = heap.GetObjectType(target);
                        Console.WriteLine(targetType.Name);
                        while (stack.Count > 0)
                        {
                            Console.WriteLine(stack.Pop());
                        }
                        Console.WriteLine();
                    }
                    else
                    {
                        FindTrace(heap, innerObject, target, visited, stack);
                    }
                });

                if (stack.Count > 0)
                {
                    stack.Pop();
                }
            }
        }
    }
}