/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using dnlib.PE;
using dnlib.DotNet.MD;
using dnlib.DotNet.Emit;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code {
	class DumpedMethodsRestorer : IRowReader<RawMethodRow>, IColumnReader, IMethodDecrypter {
		ModuleDefMD module;
		DumpedMethods dumpedMethods;

		public ModuleDefMD Module {
			set { module = value; }
		}

		public DumpedMethodsRestorer(DumpedMethods dumpedMethods) {
			this.dumpedMethods = dumpedMethods;
		}

		DumpedMethod getDumpedMethod(uint rid) {
			return dumpedMethods.get(0x06000000 | rid);
		}

		public RawMethodRow ReadRow(uint rid) {
			var dm = getDumpedMethod(rid);
			if (dm == null)
				return null;
			return new RawMethodRow(dm.mdRVA, dm.mdImplFlags, dm.mdFlags, dm.mdName, dm.mdSignature, dm.mdParamList);
		}

		public bool ReadColumn(MDTable table, uint rid, ColumnInfo column, out uint value) {
			if (table.Table == Table.Method) {
				var row = ReadRow(rid);
				if (row != null) {
					value = row.Read(column.Index);
					return true;
				}
			}

			value = 0;
			return false;
		}

		public bool HasMethodBody(uint rid) {
			return getDumpedMethod(rid) != null;
		}

		public MethodBody GetMethodBody(uint rid, RVA rva, IList<Parameter> parameters) {
			var dm = getDumpedMethod(rid);
			if (dm == null)
				return null;
			return MethodBodyReader.Create(module, dm.code, dm.extraSections, parameters, dm.mhFlags, dm.mhMaxStack, dm.mhCodeSize, dm.mhLocalVarSigTok);
		}
	}
}
