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
using dnlib.DotNet.MD;
using dnlib.DotNet;

namespace de4dot.blocks {
	[Serializable]
	public class DumpedMethods {
		Dictionary<uint, DumpedMethod> methods = new Dictionary<uint, DumpedMethod>();

		public int Count => methods.Count;
		public void Add(uint token, DumpedMethod info) => methods[token] = info;
		public DumpedMethod Get(MethodDef method) => Get(method.MDToken.ToUInt32());

		public DumpedMethod Get(uint token) {
			methods.TryGetValue(token, out var dm);
			return dm;
		}

		public void Add(DumpedMethod dm) {
			if (MDToken.ToTable(dm.token) != Table.Method || MDToken.ToRID(dm.token) == 0)
				throw new ArgumentException("Invalid token");
			methods[dm.token] = dm;
		}
	}
}
