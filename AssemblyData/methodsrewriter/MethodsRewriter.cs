/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using dnlib.DotNet.Emit;
using dnlib.DotNet;
using de4dot.blocks;

using OpCode = dnlib.DotNet.Emit.OpCode;
using OpCodes = dnlib.DotNet.Emit.OpCodes;
using ROpCode = System.Reflection.Emit.OpCode;
using ROpCodes = System.Reflection.Emit.OpCodes;

namespace AssemblyData.methodsrewriter {
	delegate object RewrittenMethod(object[] args);

	class MethodsFinder {
		Dictionary<Module, MethodsModule> moduleToMethods = new Dictionary<Module, MethodsModule>();

		class MethodsModule {
			const int MAX_METHODS = 30;
			List<MethodBase> methods = new List<MethodBase>(MAX_METHODS);
			int next;

			public MethodsModule(Module module) {
				var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

				foreach (var type in module.GetTypes()) {
					if (methods.Count >= MAX_METHODS)
						break;
					foreach (var method in type.GetMethods(flags)) {
						if (methods.Count >= MAX_METHODS)
							break;
						methods.Add(method);
					}
				}

				foreach (var method in module.GetMethods(flags)) {
					if (methods.Count >= MAX_METHODS)
						break;
					methods.Add(method);
				}
			}

			public MethodBase GetNext() {
				return methods[next++ % methods.Count];
			}
		}

		public MethodBase GetMethod(Module module) {
			MethodsModule methodsModule;
			if (!moduleToMethods.TryGetValue(module, out methodsModule))
				moduleToMethods[module] = methodsModule = new MethodsModule(module);
			return methodsModule.GetNext();
		}
	}

	class MethodsRewriter : IMethodsRewriter {
		MethodsFinder methodsFinder = new MethodsFinder();
		Dictionary<MethodBase, NewMethodInfo> realMethodToNewMethod = new Dictionary<MethodBase, NewMethodInfo>();
		Dictionary<NewMethodInfo, MethodBase> newStackMethodDict = new Dictionary<NewMethodInfo, MethodBase>();
		List<NewMethodInfo> newMethodInfos = new List<NewMethodInfo>();

		// There's no documented way to get a dynamic method's MethodInfo. If we name the
		// method and it's a unique random name, we can still find the emulated method.
		Dictionary<string, NewMethodInfo> delegateNameToNewMethodInfo = new Dictionary<string, NewMethodInfo>(StringComparer.Ordinal);

		class NewMethodInfo {
			// Original method
			public MethodBase oldMethod;

			public Type delegateType;

			// The modified code is here
			public Delegate delegateInstance;

			// newMethodInfos index
			public int delegateIndex;

			public RewrittenMethod rewrittenMethod;

			// Name of method used by delegateInstance
			public string delegateMethodName;

			// Name of method used by rewrittenMethod
			public string rewrittenMethodName;

			public NewMethodInfo(MethodBase oldMethod, int delegateIndex, string delegateMethodName, string rewrittenMethodName) {
				this.oldMethod = oldMethod;
				this.delegateIndex = delegateIndex;
				this.delegateMethodName = delegateMethodName;
				this.rewrittenMethodName = rewrittenMethodName;
			}

			public bool IsRewrittenMethod(string name) {
				return name == rewrittenMethodName;
			}

			public bool IsDelegateMethod(string name) {
				return name == delegateMethodName;
			}
		}

		public Type GetDelegateType(MethodBase methodBase) {
			return realMethodToNewMethod[methodBase].delegateType;
		}

		public RewrittenMethod CreateDelegate(MethodBase realMethod) {
			var newMethodInfo = realMethodToNewMethod[realMethod];
			if (newMethodInfo.rewrittenMethod != null)
				return newMethodInfo.rewrittenMethod;

			var dm = new DynamicMethod(newMethodInfo.rewrittenMethodName, typeof(object), new Type[] { GetType(), typeof(object[]) }, newMethodInfo.oldMethod.Module, true);
			var ilg = dm.GetILGenerator();

			ilg.Emit(ROpCodes.Ldarg_0);
			ilg.Emit(ROpCodes.Ldc_I4, newMethodInfo.delegateIndex);
			ilg.Emit(ROpCodes.Call, GetType().GetMethod("RtGetDelegateInstance", BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Instance));
			ilg.Emit(ROpCodes.Castclass, newMethodInfo.delegateType);

			var args = newMethodInfo.oldMethod.GetParameters();
			for (int i = 0; i < args.Length; i++) {
				var arg = args[i].ParameterType;

				ilg.Emit(ROpCodes.Ldarg_1);
				ilg.Emit(ROpCodes.Ldc_I4, i);
				ilg.Emit(ROpCodes.Ldelem_Ref);

				if (arg.IsValueType)
					ilg.Emit(ROpCodes.Unbox_Any, arg);
				else
					ilg.Emit(ROpCodes.Castclass, arg);
			}
			ilg.Emit(ROpCodes.Ldarg_0);

			var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance;
			var invokeMethod = newMethodInfo.delegateType.GetMethod("Invoke", flags);
			ilg.Emit(ROpCodes.Call, invokeMethod);
			if (ResolverUtils.GetReturnType(newMethodInfo.oldMethod) == typeof(void))
				ilg.Emit(ROpCodes.Ldnull);
			ilg.Emit(ROpCodes.Ret);

			newMethodInfo.rewrittenMethod = (RewrittenMethod)dm.CreateDelegate(typeof(RewrittenMethod), this);
			return newMethodInfo.rewrittenMethod;
		}

		public void SetCaller(RewrittenMethod rewrittenMethod, MethodBase caller) {
			if (caller == null)
				return;
			var newMethodInfo = GetNewMethodInfo(rewrittenMethod.Method.Name);
			newStackMethodDict[newMethodInfo] = caller;
		}

		string GetDelegateMethodName(MethodBase method) {
			string name = null;
			do {
				name = string.Format(" {0} {1:X8} DMN {2:X8} ", method.Name, method.MetadataToken, Utils.GetRandomUint());
			} while (delegateNameToNewMethodInfo.ContainsKey(name));
			return name;
		}

		public void CreateMethod(MethodBase realMethod) {
			if (realMethodToNewMethod.ContainsKey(realMethod))
				return;
			var newMethodInfo = new NewMethodInfo(realMethod, newMethodInfos.Count, GetDelegateMethodName(realMethod), GetDelegateMethodName(realMethod));
			newMethodInfos.Add(newMethodInfo);
			delegateNameToNewMethodInfo[newMethodInfo.delegateMethodName] = newMethodInfo;
			delegateNameToNewMethodInfo[newMethodInfo.rewrittenMethodName] = newMethodInfo;
			realMethodToNewMethod[realMethod] = newMethodInfo;

			var moduleInfo = Resolver.LoadAssembly(realMethod.Module);
			var methodInfo = moduleInfo.GetMethod(realMethod);
			if (!methodInfo.HasInstructions())
				throw new ApplicationException(string.Format("Method {0} ({1:X8}) has no body", methodInfo.methodDef, methodInfo.methodDef.MDToken.Raw));

			var codeGenerator = new CodeGenerator(this, newMethodInfo.delegateMethodName);
			codeGenerator.SetMethodInfo(methodInfo);
			newMethodInfo.delegateType = codeGenerator.DelegateType;

			var blocks = new Blocks(methodInfo.methodDef);
			foreach (var block in blocks.MethodBlocks.GetAllBlocks())
				Update(block, newMethodInfo);

			IList<Instruction> allInstructions;
			IList<ExceptionHandler> allExceptionHandlers;
			blocks.GetCode(out allInstructions, out allExceptionHandlers);
			newMethodInfo.delegateInstance = codeGenerator.Generate(allInstructions, allExceptionHandlers);
		}

		static Instruction Create(OpCode opcode, object operand) {
			return new Instruction {
				OpCode = opcode,
				Operand = operand,
			};
		}

		// Inserts ldarg THIS, and returns number of instructions inserted at 'i'
		int InsertLoadThis(Block block, int i) {
			block.Insert(i, Create(OpCodes.Ldarg, new Operand(Operand.Type.ThisArg)));
			return 1;
		}

		int InsertCallOurMethod(Block block, int i, string methodName) {
			block.Insert(i, Create(OpCodes.Call, new Operand(Operand.Type.OurMethod, methodName)));
			return 1;
		}

		void Update(Block block, NewMethodInfo currentMethodInfo) {
			var instrs = block.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode == OpCodes.Newobj) {
					var ctor = (IMethod)instr.Operand;
					var ctorTypeFullName = ctor.DeclaringType.FullName;
					if (ctorTypeFullName == "System.Diagnostics.StackTrace") {
						InsertLoadThis(block, i + 1);
						InsertCallOurMethod(block, i + 2, "static_RtFixStackTrace");
						i += 2;
						continue;
					}
					else if (ctorTypeFullName == "System.Diagnostics.StackFrame") {
						InsertLoadThis(block, i + 1);
						InsertCallOurMethod(block, i + 2, "static_RtFixStackFrame");
						i += 2;
						continue;
					}
				}

				if (instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) {
					var calledMethod = (IMethod)instr.Operand;
					if (calledMethod.DeclaringType.DefinitionAssembly.IsCorLib()) {
						var calledMethodFullName = calledMethod.FullName;
						if (calledMethodFullName == "System.Reflection.Assembly System.Reflection.Assembly::GetAssembly(System.Type)") {
							block.Replace(i, 1, OpCodes.Nop.ToInstruction());
							InsertLoadThis(block, i + 1);
							InsertCallOurMethod(block, i + 2, "static_RtGetAssembly_TypeArg");
							i += 2;
							continue;
						}
						else if (calledMethodFullName == "System.Reflection.Assembly System.Reflection.Assembly::GetCallingAssembly()" ||
								calledMethodFullName == "System.Reflection.Assembly System.Reflection.Assembly::GetEntryAssembly()" ||
								calledMethodFullName == "System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()") {
							block.Replace(i, 1, OpCodes.Nop.ToInstruction());
							InsertLoadThis(block, i + 1);
							block.Insert(i + 2, OpCodes.Ldc_I4.ToInstruction(currentMethodInfo.delegateIndex));
							InsertCallOurMethod(block, i + 3, "RtGetAssembly");
							i += 3;
							continue;
						}
					}

					var method = Resolver.GetMethod((IMethod)instr.Operand);
					if (method != null) {
						CreateMethod(method.methodBase);
						var newMethodInfo = realMethodToNewMethod[method.methodBase];

						block.Replace(i, 1, OpCodes.Nop.ToInstruction());
						int n = i + 1;

						// Pop all pushed args to a temp array
						var mparams = GetParameters(method.methodDef);
						if (mparams.Count > 0) {
							block.Insert(n++, OpCodes.Ldc_I4.ToInstruction(mparams.Count));
							var objectType = method.methodDef.DeclaringType.Module.CorLibTypes.Object;
							block.Insert(n++, OpCodes.Newarr.ToInstruction(objectType));
							block.Insert(n++, Create(OpCodes.Stloc, new Operand(Operand.Type.TempObjArray)));

							for (int j = mparams.Count - 1; j >= 0; j--) {
								var argType = mparams[j];
								if (argType.RemovePinnedAndModifiers().IsValueType)
									block.Insert(n++, OpCodes.Box.ToInstruction(((TypeDefOrRefSig)argType).TypeDefOrRef));
								block.Insert(n++, Create(OpCodes.Stloc, new Operand(Operand.Type.TempObj)));
								block.Insert(n++, Create(OpCodes.Ldloc, new Operand(Operand.Type.TempObjArray)));
								block.Insert(n++, OpCodes.Ldc_I4.ToInstruction(j));
								block.Insert(n++, Create(OpCodes.Ldloc, new Operand(Operand.Type.TempObj)));
								block.Insert(n++, OpCodes.Stelem_Ref.ToInstruction());
							}
						}

						// Push delegate instance
						InsertLoadThis(block, n++);
						block.Insert(n++, OpCodes.Ldc_I4.ToInstruction(newMethodInfo.delegateIndex));
						InsertCallOurMethod(block, n++, "RtGetDelegateInstance");
						block.Insert(n++, Create(OpCodes.Castclass, new Operand(Operand.Type.ReflectionType, newMethodInfo.delegateType)));

						// Push all popped args
						if (mparams.Count > 0) {
							for (int j = 0; j < mparams.Count; j++) {
								block.Insert(n++, Create(OpCodes.Ldloc, new Operand(Operand.Type.TempObjArray)));
								block.Insert(n++, OpCodes.Ldc_I4.ToInstruction(j));
								block.Insert(n++, OpCodes.Ldelem_Ref.ToInstruction());
								var argType = mparams[j];
								if (argType.RemovePinnedAndModifiers().IsValueType)
									block.Insert(n++, OpCodes.Unbox_Any.ToInstruction(((TypeDefOrRefSig)argType).TypeDefOrRef));
								else {
									// Don't cast it to its correct type. This will sometimes cause
									// an exception in some EF obfuscated assembly since we'll be
									// trying to cast a System.Reflection.AssemblyName type to some
									// other type.
									// block.insert(n++, Instruction.Create(OpCodes.Castclass, argType.ToTypeDefOrRef()));
								}
							}
						}

						InsertLoadThis(block, n++);
						block.Insert(n++, Create(OpCodes.Call, new Operand(Operand.Type.NewMethod, method.methodBase)));
						i = n - 1;
						continue;
					}
				}
			}
		}

		static IList<TypeSig> GetParameters(MethodDef method) {
			var list = new List<TypeSig>(method.Parameters.Count);
			for (int i = 0; i < method.Parameters.Count; i++)
				list.Add(method.Parameters[i].Type);
			return list;
		}

		static FieldInfo GetStackTraceStackFramesField() {
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			return ResolverUtils.GetFieldThrow(typeof(StackTrace), typeof(StackFrame[]), flags, "Could not find StackTrace's frames (StackFrame[]) field");
		}

		static FieldInfo GetStackFrameMethodField() {
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			return ResolverUtils.GetFieldThrow(typeof(StackFrame), typeof(MethodBase), flags, "Could not find StackFrame's method (MethodBase) field");
		}

		static void WriteMethodBase(StackFrame frame, MethodBase method) {
			var methodField = GetStackFrameMethodField();
			methodField.SetValue(frame, method);
			if (frame.GetMethod() != method)
				throw new ApplicationException(string.Format("Could not set new method: {0}", method));
		}

		NewMethodInfo GetNewMethodInfo(string name) {
			NewMethodInfo info;
			delegateNameToNewMethodInfo.TryGetValue(name, out info);
			return info;
		}

		// Called after the StackTrace ctor has been called.
		static StackTrace static_RtFixStackTrace(StackTrace stackTrace, MethodsRewriter self) {
			return self.RtFixStackTrace(stackTrace);
		}

		StackTrace RtFixStackTrace(StackTrace stackTrace) {
			var framesField = GetStackTraceStackFramesField();
			var frames = (StackFrame[])framesField.GetValue(stackTrace);

			var newFrames = new List<StackFrame>(frames.Length);
			foreach (var frame in frames) {
				FixStackFrame(frame);
				newFrames.Add(frame);
			}

			framesField.SetValue(stackTrace, newFrames.ToArray());
			return stackTrace;
		}

		static StackFrame static_RtFixStackFrame(StackFrame stackFrame, MethodsRewriter self) {
			return self.RtFixStackFrame(stackFrame);
		}

		StackFrame RtFixStackFrame(StackFrame frame) {
			FixStackFrame(frame);
			return frame;
		}

		void FixStackFrame(StackFrame frame) {
			var method = frame.GetMethod();
			var info = GetNewMethodInfo(method.Name);
			if (info == null)
				return;

			MethodBase stackMethod;
			if (newStackMethodDict.TryGetValue(info, out stackMethod)) {
				WriteMethodBase(frame, stackMethod);
			}
			else if (info.IsRewrittenMethod(method.Name)) {
				// Write random method from the same module
				WriteMethodBase(frame, methodsFinder.GetMethod(info.oldMethod.Module));
			}
			else if (info.IsDelegateMethod(method.Name)) {
				// Write original method
				WriteMethodBase(frame, info.oldMethod);
			}
			else
				throw new ApplicationException("BUG: Shouldn't be here");
		}

		// Called when the code calls GetCallingAssembly(), GetEntryAssembly(), or GetExecutingAssembly()
		Assembly RtGetAssembly(int delegateIndex) {
			return newMethodInfos[delegateIndex].oldMethod.Module.Assembly;
		}

		// Called when the code calls GetAssembly(Type)
		static Assembly static_RtGetAssembly_TypeArg(Type type, MethodsRewriter self) {
			return self.RtGetAssembly_TypeArg(type);
		}

		Assembly RtGetAssembly_TypeArg(Type type) {
			return Assembly.GetAssembly(type);
		}

		Delegate RtGetDelegateInstance(int delegateIndex) {
			return newMethodInfos[delegateIndex].delegateInstance;
		}
	}
}
