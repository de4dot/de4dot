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

using System.Collections.Generic;
using dnlib.DotNet;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class ArrayValueInliner : ValueInlinerBase<byte[]> {
		InitializedDataCreator initializedDataCreator;
		ModuleDefMD module;

		public ArrayValueInliner(ModuleDefMD module, InitializedDataCreator initializedDataCreator) {
			this.module = module;
			this.initializedDataCreator = initializedDataCreator;
		}

		protected override void InlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				var arrayData = (byte[])callResult.returnValue;
				initializedDataCreator.AddInitializeArrayCode(block, callResult.callStartIndex, num, module.CorLibTypes.Byte.TypeDefOrRef, arrayData);
				Logger.v("Decrypted array: {0} bytes", arrayData.Length);
			}
		}
	}
}
