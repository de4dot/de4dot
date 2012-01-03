/*
    Copyright (C) 2011 de4dot@gmail.com

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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	abstract class ValueInlinerBase<TValue> : MethodReturnValueInliner {
		MethodDefinitionAndDeclaringTypeDict<Func<MethodDefinition, object[], TValue>> decrypterMethods = new MethodDefinitionAndDeclaringTypeDict<Func<MethodDefinition, object[], TValue>>();

		class MyCallResult : CallResult {
			public MethodReference methodReference;
			public MyCallResult(Block block, int callEndIndex, MethodReference method)
				: base(block, callEndIndex) {
				this.methodReference = method;
			}
		}

		public bool HasHandlers {
			get { return decrypterMethods.Count != 0; }
		}

		public IEnumerable<MethodDefinition> Methods {
			get { return decrypterMethods.getKeys(); }
		}

		public void add(MethodDefinition method, Func<MethodDefinition, object[], TValue> handler) {
			if (method != null)
				decrypterMethods.add(method, handler);
		}

		protected override void inlineAllCalls() {
			foreach (var tmp in callResults) {
				var callResult = (MyCallResult)tmp;
				var handler = decrypterMethods.find(callResult.methodReference);
				callResult.returnValue = handler((MethodDefinition)callResult.methodReference, callResult.args);
			}
		}

		protected override CallResult createCallResult(MethodReference method, Block block, int callInstrIndex) {
			if (decrypterMethods.find(method) == null)
				return null;
			return new MyCallResult(block, callInstrIndex, method);
		}
	}

	class BooleanValueInliner : ValueInlinerBase<bool> {
		protected override void inlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.replace(callResult.callStartIndex, num, DotNetUtils.createLdci4((bool)callResult.returnValue ? 1 : 0));
				Log.v("Decrypted boolean: {0}", callResult.returnValue);
			}
		}
	}

	class Int32ValueInliner : ValueInlinerBase<int> {
		protected override void inlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.replace(callResult.callStartIndex, num, DotNetUtils.createLdci4((int)callResult.returnValue));
				Log.v("Decrypted int32: {0}", callResult.returnValue);
			}
		}
	}

	class Int64ValueInliner : ValueInlinerBase<long> {
		protected override void inlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.replace(callResult.callStartIndex, num, Instruction.Create(OpCodes.Ldc_I8, (long)callResult.returnValue));
				Log.v("Decrypted int64: {0}", callResult.returnValue);
			}
		}
	}

	class SingleValueInliner : ValueInlinerBase<float> {
		protected override void inlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.replace(callResult.callStartIndex, num, Instruction.Create(OpCodes.Ldc_R4, (float)callResult.returnValue));
				Log.v("Decrypted single: {0}", callResult.returnValue);
			}
		}
	}

	class DoubleValueInliner : ValueInlinerBase<double> {
		protected override void inlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.replace(callResult.callStartIndex, num, Instruction.Create(OpCodes.Ldc_R8, (double)callResult.returnValue));
				Log.v("Decrypted double: {0}", callResult.returnValue);
			}
		}
	}
}
