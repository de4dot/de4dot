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

using System;
using System.Collections.Generic;
using System.IO;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Babel_NET {
	class BabelMethodreference {
		public string Name { get; set; }
		public TypeSig DeclaringType { get; set; }
		public TypeSig ReturnType { get; set; }
		public Parameter[] Parameters { get; set; }
		public TypeSig[] GenericArguments { get; set; }
		public int Flags { get; set; }

		public bool HasThis {
			get { return (Flags & 1) != 0; }
		}

		public bool IsGenericMethod {
			get { return (Flags & 2) != 0; }
		}
	}

	class BabelMethodDef : BabelMethodreference {
		Parameter thisParameter;

		public int Flags2 { get; set; }
		public ushort MaxStack { get; set; }
		public IList<Local> Locals { get; set; }
		public IList<Instruction> Instructions { get; set; }
		public IList<ExceptionHandler> ExceptionHandlers { get; set; }

		public bool IsStatic {
			get { return (Flags2 & 0x10) != 0; }
		}

		public bool RequiresFatExceptionHandler {
			get { return (Flags2 & 0x20) != 0; }
		}

		public bool InitLocals {
			get { return (Flags2 & 0x40) != 0; }
		}

		public bool CacheMethod {
			get { return (Flags2 & 0x80) != 0; }
		}

		public Parameter ThisParameter {
			get {
				if (!HasThis)
					return null;
				if (thisParameter != null)
					return thisParameter;
				return thisParameter = new Parameter(0, Parameter.HIDDEN_THIS_METHOD_SIG_INDEX, DeclaringType);
			}
		}

		public void setBody(MethodBodyReader mbr) {
			Flags2 = mbr.Flags2;
			MaxStack = mbr.MaxStack;
			Locals = mbr.Locals;
			Instructions = mbr.Instructions;
			ExceptionHandlers = mbr.ExceptionHandlers;
		}

		public IList<Parameter> getRealParameters() {
			if (ThisParameter == null)
				return Parameters;
			var parameters = new Parameter[Parameters.Length + 1];
			parameters[0] = ThisParameter;
			Array.Copy(Parameters, 0, parameters, 1, Parameters.Length);
			return parameters;
		}
	}

	class MethodRefReader {
		ImageReader imageReader;
		IBinaryReader reader;
		BabelMethodreference bmr;

		public MethodRefReader(ImageReader imageReader, IBinaryReader reader)
			: this(imageReader, reader, new BabelMethodreference()) {
		}

		public MethodRefReader(ImageReader imageReader, IBinaryReader reader, BabelMethodreference bmr) {
			this.imageReader = imageReader;
			this.reader = reader;
			this.bmr = bmr;
		}

		public BabelMethodreference read() {
			bmr.Name = imageReader.readString();
			bmr.DeclaringType = imageReader.readTypeSig();
			bmr.ReturnType = imageReader.readTypeSig();
			var argTypes = imageReader.readTypeSigs();
			bmr.Flags = reader.ReadByte();
			if (bmr.IsGenericMethod)
				bmr.GenericArguments = imageReader.readTypeSigs();
			else
				bmr.GenericArguments = new TypeSig[0];
			bmr.Parameters = readParameters(argTypes, bmr.HasThis);
			return bmr;
		}

		Parameter[] readParameters(IList<TypeSig> argTypes, bool hasThis) {
			var ps = new Parameter[argTypes.Count];
			int bi = hasThis ? 1 : 0;
			for (int i = 0; i < ps.Length; i++)
				ps[i] = new Parameter(bi + i, i, argTypes[i]);
			return ps;
		}
	}

	class MethodDefReader {
		MethodRefReader methodRefReader;
		MethodBodyReader methodBodyReader;
		BabelMethodDef bmd;

		public MethodDefReader(ImageReader imageReader, IBinaryReader reader) {
			this.bmd = new BabelMethodDef();
			this.methodRefReader = new MethodRefReader(imageReader, reader, bmd);
			this.methodBodyReader = new MethodBodyReader(imageReader, reader);
		}

		public BabelMethodDef read() {
			methodRefReader.read();
			methodBodyReader.read(bmd.getRealParameters());
			bmd.setBody(methodBodyReader);
			return bmd;
		}
	}
}
