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

namespace de4dot.code.deobfuscators {
	public abstract class ValueInlinerBase<TValue> : MethodReturnValueInliner {
		MethodDefAndDeclaringTypeDict<Func<MethodDef, MethodSpec, object[], object>> decrypterMethods = new MethodDefAndDeclaringTypeDict<Func<MethodDef, MethodSpec, object[], object>>();
		bool removeUnbox = false;

		class MyCallResult : CallResult {
			public IMethod methodRef;
			public MethodSpec gim;
			public MyCallResult(Block block, int callEndIndex, IMethod method, MethodSpec gim)
				: base(block, callEndIndex) {
				methodRef = method;
				this.gim = gim;
			}
		}

		public bool RemoveUnbox {
			get => removeUnbox;
			set => removeUnbox = value;
		}

		public override bool HasHandlers => decrypterMethods.Count != 0;
		public IEnumerable<MethodDef> Methods => decrypterMethods.GetKeys();

		public void Add(MethodDef method, Func<MethodDef, MethodSpec, object[], object> handler) {
			if (method == null)
				return;
			if (decrypterMethods.Find(method) != null)
				throw new ApplicationException($"Handler for method {method.MDToken.ToInt32():X8} has already been added");
			if (method != null)
				decrypterMethods.Add(method, handler);
		}

		protected override void InlineAllCalls() {
			foreach (var tmp in callResults) {
				var callResult = (MyCallResult)tmp;
				var handler = decrypterMethods.Find(callResult.methodRef);
				callResult.returnValue = handler((MethodDef)callResult.methodRef, callResult.gim, callResult.args);
			}
		}

		protected override CallResult CreateCallResult(IMethod method, MethodSpec gim, Block block, int callInstrIndex) {
			if (decrypterMethods.Find(method) == null)
				return null;
			return new MyCallResult(block, callInstrIndex, method, gim);
		}

		protected bool RemoveUnboxInstruction(Block block, int index, string unboxType) {
			if (!removeUnbox)
				return false;
			var instrs = block.Instructions;
			if (index >= instrs.Count)
				return false;
			var unbox = instrs[index];
			if (unbox.OpCode.Code != Code.Unbox_Any)
				return false;
			var type = unbox.Operand as ITypeDefOrRef;
			if (type == null || type.FullName != unboxType)
				return false;
			block.Remove(index, 1);
			return true;
		}
	}

	public class BooleanValueInliner : ValueInlinerBase<bool> {
		protected override void InlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.Replace(callResult.callStartIndex, num, Instruction.CreateLdcI4((bool)callResult.returnValue ? 1 : 0));
				RemoveUnboxInstruction(block, callResult.callStartIndex + 1, "System.Boolean");
				Logger.v("Decrypted boolean: {0}", callResult.returnValue);
			}
		}
	}

	public class Int32ValueInliner : ValueInlinerBase<int> {
		protected override void InlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.Replace(callResult.callStartIndex, num, Instruction.CreateLdcI4((int)callResult.returnValue));
				RemoveUnboxInstruction(block, callResult.callStartIndex + 1, "System.Int32");
				Logger.v("Decrypted int32: {0}", callResult.returnValue);
			}
		}
	}

	public class Int64ValueInliner : ValueInlinerBase<long> {
		protected override void InlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.Replace(callResult.callStartIndex, num, OpCodes.Ldc_I8.ToInstruction((long)callResult.returnValue));
				RemoveUnboxInstruction(block, callResult.callStartIndex + 1, "System.Int64");
				Logger.v("Decrypted int64: {0}", callResult.returnValue);
			}
		}
	}

	public class SingleValueInliner : ValueInlinerBase<float> {
		protected override void InlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.Replace(callResult.callStartIndex, num, OpCodes.Ldc_R4.ToInstruction((float)callResult.returnValue));
				RemoveUnboxInstruction(block, callResult.callStartIndex + 1, "System.Single");
				Logger.v("Decrypted single: {0}", callResult.returnValue);
			}
		}
	}

	public class DoubleValueInliner : ValueInlinerBase<double> {
		protected override void InlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.Replace(callResult.callStartIndex, num, OpCodes.Ldc_R8.ToInstruction((double)callResult.returnValue));
				RemoveUnboxInstruction(block, callResult.callStartIndex + 1, "System.Double");
				Logger.v("Decrypted double: {0}", callResult.returnValue);
			}
		}
	}
}
