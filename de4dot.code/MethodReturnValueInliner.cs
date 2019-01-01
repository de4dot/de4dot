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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code {
	// A simple class that statically detects the values of some local variables
	public class VariableValues {
		IList<Block> allBlocks;
		IList<Local> locals;
		Dictionary<Local, Variable> variableToValue = new Dictionary<Local, Variable>();

		public class Variable {
			int writes = 0;
			object value;
			bool unknownValue = false;

			public bool IsValid() => !unknownValue && writes == 1;

			public object Value {
				get {
					if (!IsValid())
						throw new ApplicationException("Unknown variable value");
					return value;
				}
				set => this.value = value;
			}

			public void AddWrite() => writes++;
			public void SetUnknown() => unknownValue = true;
		}

		public VariableValues(IList<Local> locals, IList<Block> allBlocks) {
			this.locals = locals;
			this.allBlocks = allBlocks;
			Initialize();
		}

		void Initialize() {
			foreach (var variable in locals)
				variableToValue[variable] = new Variable();

			foreach (var block in allBlocks) {
				for (int i = 0; i < block.Instructions.Count; i++) {
					var instr = block.Instructions[i];

					switch (instr.OpCode.Code) {
					case Code.Stloc:
					case Code.Stloc_S:
					case Code.Stloc_0:
					case Code.Stloc_1:
					case Code.Stloc_2:
					case Code.Stloc_3:
						var variable = Instr.GetLocalVar(locals, instr);
						var val = variableToValue[variable];
						val.AddWrite();
						object obj;
						if (!GetValue(block, i, out obj))
							val.SetUnknown();
						val.Value = obj;
						break;

					default:
						break;
					}
				}
			}
		}

		bool GetValue(Block block, int index, out object obj) {
			while (true) {
				if (index <= 0) {
					obj = null;
					return false;
				}
				var instr = block.Instructions[--index];
				if (instr.OpCode == OpCodes.Nop)
					continue;

				switch (instr.OpCode.Code) {
				case Code.Ldc_I4:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldstr:
					obj = instr.Operand;
					return true;
				case Code.Ldc_I4_S:
					obj = (int)(sbyte)instr.Operand;
					return true;

				case Code.Ldc_I4_0: obj = 0; return true;
				case Code.Ldc_I4_1: obj = 1; return true;
				case Code.Ldc_I4_2: obj = 2; return true;
				case Code.Ldc_I4_3: obj = 3; return true;
				case Code.Ldc_I4_4: obj = 4; return true;
				case Code.Ldc_I4_5: obj = 5; return true;
				case Code.Ldc_I4_6: obj = 6; return true;
				case Code.Ldc_I4_7: obj = 7; return true;
				case Code.Ldc_I4_8: obj = 8; return true;
				case Code.Ldc_I4_M1:obj = -1; return true;
				case Code.Ldnull:	obj = null; return true;

				default:
					obj = null;
					return false;
				}
			}
		}

		public Variable GetValue(Local variable) => variableToValue[variable];
	}

	public abstract class MethodReturnValueInliner {
		protected List<CallResult> callResults;
		List<Block> allBlocks;
		MethodDef theMethod;
		VariableValues variableValues;
		int errors = 0;
		bool useUnknownArgs = false;

		public bool UseUnknownArgs {
			get => useUnknownArgs;
			set => useUnknownArgs = value;
		}

		protected class CallResult {
			public Block block;
			public int callStartIndex;
			public int callEndIndex;
			public object[] args;
			public object returnValue;

			public CallResult(Block block, int callEndIndex) {
				this.block = block;
				this.callEndIndex = callEndIndex;
			}

			public IMethod GetMethodRef() => (IMethod)block.Instructions[callEndIndex].Operand;
		}

		public bool InlinedAllCalls => errors == 0;
		public abstract bool HasHandlers { get; }
		public MethodDef Method => theMethod;

		protected abstract void InlineAllCalls();

		// Returns null if method is not a method we should inline
		protected abstract CallResult CreateCallResult(IMethod method, MethodSpec gim, Block block, int callInstrIndex);

		public int Decrypt(Blocks blocks) {
			if (!HasHandlers)
				return 0;
			return Decrypt(blocks.Method, blocks.MethodBlocks.GetAllBlocks());
		}

		public int Decrypt(MethodDef method, List<Block> allBlocks) {
			if (!HasHandlers)
				return 0;
			try {
				theMethod = method;
				callResults = new List<CallResult>();
				this.allBlocks = allBlocks;

				FindAllCallResults();
				InlineAllCalls();
				InlineReturnValues();
				return callResults.Count;
			}
			catch {
				errors++;
				throw;
			}
			finally {
				theMethod = null;
				callResults = null;
				this.allBlocks = null;
				variableValues = null;
			}
		}

		bool GetLocalVariableValue(Local variable, out object value) {
			if (variableValues == null)
				variableValues = new VariableValues(theMethod.Body.Variables, allBlocks);
			var val = variableValues.GetValue(variable);
			if (!val.IsValid()) {
				value = null;
				return false;
			}
			value = val.Value;
			return true;
		}

		void FindAllCallResults() {
			foreach (var block in allBlocks)
				FindCallResults(block);
		}

		void FindCallResults(Block block) {
			for (int i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];
				if (instr.OpCode != OpCodes.Call)
					continue;
				var method = instr.Operand as IMethod;
				if (method == null)
					continue;

				var elementMethod = method;
				var gim = method as MethodSpec;
				if (gim != null)
					elementMethod = gim.Method;
				var callResult = CreateCallResult(elementMethod, gim, block, i);
				if (callResult == null)
					continue;

				if (FindArgs(callResult))
					callResults.Add(callResult);
			}
		}

		bool FindArgs(CallResult callResult) {
			var block = callResult.block;
			var method = callResult.GetMethodRef();
			var methodArgs = DotNetUtils.GetArgs(method);
			int numArgs = methodArgs.Count;
			var args = new object[numArgs];

			int instrIndex = callResult.callEndIndex - 1;
			for (int i = numArgs - 1; i >= 0; i--) {
				object arg = null;
				if (!GetArg(method, block, ref arg, ref instrIndex))
					return false;
				if (arg is int)
					arg = FixIntArg(methodArgs[i], (int)arg);
				else if (arg is long)
					arg = FixIntArg(methodArgs[i], (long)arg);
				args[i] = arg;
			}

			callResult.args = args;
			callResult.callStartIndex = instrIndex + 1;
			return true;
		}

		object FixIntArg(TypeSig type, long value) {
			switch (type.ElementType) {
			case ElementType.Boolean: return value != 0;
			case ElementType.Char: return (char)value;
			case ElementType.I1: return (sbyte)value;
			case ElementType.U1: return (byte)value;
			case ElementType.I2: return (short)value;
			case ElementType.U2: return (ushort)value;
			case ElementType.I4: return (int)value;
			case ElementType.U4: return (uint)value;
			case ElementType.I8: return (long)value;
			case ElementType.U8: return (ulong)value;
			}
			throw new ApplicationException($"Wrong type {type}");
		}

		bool GetArg(IMethod method, Block block, ref object arg, ref int instrIndex) {
			while (true) {
				if (instrIndex < 0) {
					// We're here if there were no cflow deobfuscation, or if there are two or
					// more blocks branching to the decrypter method, or the two blocks can't be
					// merged because one is outside the exception handler (eg. buggy obfuscator).
					Logger.w("Could not find all arguments to method {0} ({1:X8})",
								Utils.RemoveNewlines(method),
								method.MDToken.ToInt32());
					errors++;
					return false;
				}

				var instr = block.Instructions[instrIndex--];
				switch (instr.OpCode.Code) {
				case Code.Ldc_I4:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldstr:
					arg = instr.Operand;
					break;
				case Code.Ldc_I4_S:
					arg = (int)(sbyte)instr.Operand;
					break;

				case Code.Ldc_I4_0: arg = 0; break;
				case Code.Ldc_I4_1: arg = 1; break;
				case Code.Ldc_I4_2: arg = 2; break;
				case Code.Ldc_I4_3: arg = 3; break;
				case Code.Ldc_I4_4: arg = 4; break;
				case Code.Ldc_I4_5: arg = 5; break;
				case Code.Ldc_I4_6: arg = 6; break;
				case Code.Ldc_I4_7: arg = 7; break;
				case Code.Ldc_I4_8: arg = 8; break;
				case Code.Ldc_I4_M1:arg = -1; break;
				case Code.Ldnull:	arg = null; break;

				case Code.Nop:
					continue;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					GetLocalVariableValue(instr.Instruction.GetLocal(theMethod.Body.Variables), out arg);
					break;

				case Code.Ldfld:
				case Code.Ldsfld:
					arg = instr.Operand;
					break;

				default:
					int pushes, pops;
					instr.Instruction.CalculateStackUsage(false, out pushes, out pops);
					if (!useUnknownArgs || pushes != 1) {
						Logger.w("Could not find all arguments to method {0} ({1:X8}), instr: {2}",
									Utils.RemoveNewlines(method),
									method.MDToken.ToInt32(),
									instr);
						errors++;
						return false;
					}

					for (int i = 0; i < pops; i++) {
						if (!GetArg(method, block, ref arg, ref instrIndex))
							return false;
					}
					arg = null;
					break;
				}
				break;
			}

			return true;
		}

		void InlineReturnValues() {
			callResults = RemoveNulls(callResults);
			callResults.Sort((a, b) => {
				int i1 = allBlocks.FindIndex((x) => a.block == x);
				int i2 = allBlocks.FindIndex((x) => b.block == x);
				if (i1 != i2)
					return i1.CompareTo(i2);

				return a.callStartIndex.CompareTo(b.callStartIndex);
			});
			callResults.Reverse();
			InlineReturnValues(callResults);
		}

		static List<CallResult> RemoveNulls(List<CallResult> inList) {
			var outList = new List<CallResult>(inList.Count);
			foreach (var callResult in inList) {
				if (callResult.returnValue != null)
					outList.Add(callResult);
			}
			return outList;
		}

		protected abstract void InlineReturnValues(IList<CallResult> callResults);
	}
}
