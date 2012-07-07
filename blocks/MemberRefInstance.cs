/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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

// Create a new type, method, etc, where all generic parameters have been replaced with the
// corresponding generic argument.

using System;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.blocks {
	public class TypeReferenceInstance : TypeReferenceUpdaterBase {
		TypeReference typeReference;
		GenericInstanceType git;
		IGenericInstance gim;
		bool modified;

		public static TypeReference make(TypeReference typeReference, GenericInstanceType git) {
			return make(typeReference, git, null);
		}

		public static TypeReference make(TypeReference typeReference, GenericInstanceType git, IGenericInstance gim) {
			if (git == null && gim == null)
				return typeReference;
			return new TypeReferenceInstance(typeReference, git, gim).makeInstance();
		}

		TypeReferenceInstance(TypeReference typeReference, GenericInstanceType git, IGenericInstance gim) {
			this.typeReference = typeReference;
			this.git = git;
			this.gim = gim;
		}

		void checkModified(object a, object b) {
			if (!ReferenceEquals(a, b))
				modified = true;
		}

		// Returns same one if nothing was modified
		TypeReference makeInstance() {
			var rv = update(typeReference);
			return modified ? rv : typeReference;
		}

		protected override FunctionPointerType updateFunctionPointerType(FunctionPointerType a) {
			var rv = new FunctionPointerType();
			rv.function = MethodReferenceInstance.make(a.function, git, gim);
			checkModified(a.function, rv.function);
			return rv;
		}

		protected override TypeReference updateGenericParameter(GenericParameter a) {
			switch (a.Type) {
			case GenericParameterType.Type:
				if (git == null || a.Position >= git.GenericArguments.Count ||
					!MemberReferenceHelper.compareTypes(git.ElementType, a.Owner as TypeReference)) {
					return a;
				}
				modified = true;
				return update(git.GenericArguments[a.Position]);

			case GenericParameterType.Method:
				if (gim == null || a.Position >= gim.GenericArguments.Count)
					return a;
				modified = true;
				return update(gim.GenericArguments[a.Position]);

			default:
				return a;
			}
		}
	}

	public abstract class MultiTypeRefInstance {
		GenericInstanceType git;
		IGenericInstance gim;
		bool modified;

		public MultiTypeRefInstance(GenericInstanceType git)
			: this(git, null) {
		}

		public MultiTypeRefInstance(GenericInstanceType git, IGenericInstance gim) {
			this.git = git;
			this.gim = gim;
		}

		void checkModified(object a, object b) {
			if (!ReferenceEquals(a, b))
				modified = true;
		}

		protected TypeReference makeInstance(TypeReference tr) {
			var type = TypeReferenceInstance.make(tr, git, gim);
			checkModified(type, tr);
			return type;
		}

		protected T getResult<T>(T orig, T newOne) {
			return modified ? newOne : orig;
		}
	}

	public class MethodReferenceInstance : MultiTypeRefInstance {
		MethodReference methodReference;

		public static MethodReference make(MethodReference methodReference, GenericInstanceType git) {
			return make(methodReference, git, null);
		}

		public static MethodReference make(MethodReference methodReference, GenericInstanceType git, IGenericInstance gim) {
			if (git == null && gim == null)
				return methodReference;
			return new MethodReferenceInstance(methodReference, git, gim).makeInstance();
		}

		MethodReferenceInstance(MethodReference methodReference, GenericInstanceType git, IGenericInstance gim)
			: base(git, gim) {
			this.methodReference = methodReference;
		}

		MethodReference makeInstance() {
			var mr = new MethodReference(methodReference.Name, makeInstance(methodReference.MethodReturnType.ReturnType), methodReference.DeclaringType);
			mr.HasThis = methodReference.HasThis;
			mr.ExplicitThis = methodReference.ExplicitThis;
			mr.CallingConvention = methodReference.CallingConvention;

			if (methodReference.HasParameters) {
				foreach (var param in methodReference.Parameters) {
					var newParam = new ParameterDefinition(param.Name, param.Attributes, makeInstance(param.ParameterType));
					mr.Parameters.Add(newParam);
				}
			}

			if (methodReference.HasGenericParameters) {
				foreach (var param in methodReference.GenericParameters) {
					var newParam = new GenericParameter(param.Name, mr);
					mr.GenericParameters.Add(newParam);
				}
			}

			return getResult(methodReference, mr);
		}
	}

	public class FieldReferenceInstance : MultiTypeRefInstance {
		FieldReference fieldReference;

		public static FieldReference make(FieldReference fieldReference, GenericInstanceType git) {
			if (git == null)
				return fieldReference;
			return new FieldReferenceInstance(fieldReference, git).makeInstance();
		}

		FieldReferenceInstance(FieldReference fieldReference, GenericInstanceType git)
			: base(git) {
			this.fieldReference = fieldReference;
		}

		FieldReference makeInstance() {
			var fr = new FieldReference(fieldReference.Name, makeInstance(fieldReference.FieldType));
			return getResult(fieldReference, fr);
		}
	}

	public class EventReferenceInstance : MultiTypeRefInstance {
		EventReference eventReference;

		public static EventReference make(EventReference eventReference, GenericInstanceType git) {
			if (git == null)
				return eventReference;
			return new EventReferenceInstance(eventReference, git).makeInstance();
		}

		EventReferenceInstance(EventReference eventReference, GenericInstanceType git)
			: base(git) {
			this.eventReference = eventReference;
		}

		EventReference makeInstance() {
			var er = new EventDefinition(eventReference.Name, (EventAttributes)0, makeInstance(eventReference.EventType));
			return getResult(eventReference, er);
		}
	}

	public class PropertyReferenceInstance : MultiTypeRefInstance {
		PropertyReference propertyReference;

		public static PropertyReference make(PropertyReference propertyReference, GenericInstanceType git) {
			if (git == null)
				return propertyReference;
			return new PropertyReferenceInstance(propertyReference, git).makeInstance();
		}

		PropertyReferenceInstance(PropertyReference propertyReference, GenericInstanceType git)
			: base(git) {
			this.propertyReference = propertyReference;
		}

		PropertyReference makeInstance() {
			var pr = new PropertyDefinition(propertyReference.Name, (PropertyAttributes)0, makeInstance(propertyReference.PropertyType));
			if (propertyReference.Parameters != null) {
				foreach (var param in propertyReference.Parameters) {
					var newParam = new ParameterDefinition(param.Name, param.Attributes, makeInstance(param.ParameterType));
					pr.Parameters.Add(newParam);
				}
			}

			return getResult(propertyReference, pr);
		}
	}
}
