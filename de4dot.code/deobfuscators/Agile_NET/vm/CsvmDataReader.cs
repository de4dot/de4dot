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
using dnlib.IO;

namespace de4dot.code.deobfuscators.Agile_NET.vm {
	class CsvmDataReader {
		DataReader reader;

		public CsvmDataReader(DataReader reader) {
			reader.Position = 0;
			this.reader = reader;
		}

		public List<CsvmMethodData> Read() {
			int numMethods = reader.ReadInt32();
			if (numMethods < 0)
				throw new ApplicationException("Invalid number of methods");
			var methods = new List<CsvmMethodData>(numMethods);

			for (int i = 0; i < numMethods; i++) {
				var csvmMethod = new CsvmMethodData();
				csvmMethod.Guid = new Guid(reader.ReadBytes(16));
				csvmMethod.Token = reader.ReadInt32();
				csvmMethod.Locals = reader.ReadBytes(reader.ReadInt32());
				csvmMethod.Instructions = reader.ReadBytes(reader.ReadInt32());
				csvmMethod.Exceptions = reader.ReadBytes(reader.ReadInt32());
				methods.Add(csvmMethod);
			}

			return methods;
		}
	}
}
