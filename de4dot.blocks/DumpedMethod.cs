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

namespace de4dot.blocks {
	[Serializable]
	public class DumpedMethod {
		public ushort mhFlags;			// method header Flags
		public ushort mhMaxStack;		// method header MaxStack
		public uint mhCodeSize;			// method header CodeSize
		public uint mhLocalVarSigTok;	// method header LocalVarSigTok

		public uint mdRVA;				// methodDef RVA
		public ushort mdImplFlags;		// methodDef ImplFlags
		public ushort mdFlags;			// methodDef Flags
		public uint mdName;				// methodDef Name (index into #String)
		public uint mdSignature;		// methodDef Signature (index into #Blob)
		public uint mdParamList;		// methodDef ParamList (index into Param table)

		public uint token;				// metadata token

		public byte[] code;
		public byte[] extraSections;
	}
}
