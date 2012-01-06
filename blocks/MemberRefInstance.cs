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

// Create a new type, method, etc, where all generic parameters have been replaced with the
// corresponding generic argument.

using System;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.blocks {
	public abstract class RefInstance {
		protected bool modified = false;

		protected void checkModified(object a, object b) {
			if (!ReferenceEquals(a, b))
				modified = true;
		}
	}

	public class TypeReferenceInstance : RefInstance {
		TypeReference typeReference;
		GenericInstanceType git;
		IGenericInstance gim;

		public static TypeReference make(TypeReference typeReference, GenericInstanceType git, IGenericInstance gim = null) {
			if (git == null && gim == null)
				return typeReference;
			return new TypeReferenceInstance(typeReference, git, gim).makeInstance();
		}

		TypeReferenceInstance(TypeReference typeReference, GenericInstanceType git, IGenericInstance gim) {
			this.typeReference = typeReference;
			this.git = git;
			this.gim = gim;
		}

		// Returns same one if nothing was modified
		TypeReference makeInstance() {
			var rv = makeInstance(typeReference);
			return modified ? rv : typeReference;
		}

		TypeReference makeInstance(TypeReference a) {
			if (a == null)
				return null;

			var type = MemberReferenceHelper.getMemberReferenceType(a);
			switch (type) {
			case CecilType.ArrayType:
				return makeInstanceArrayType((ArrayType)a);
			case CecilType.ByReferenceType:
				return makeInstanceByReferenceType((ByReferenceType)a);
			case CecilType.FunctionPointerType:
				return makeInstanceFunctionPointerType((FunctionPointerType)a);
			case CecilType.GenericInstanceType:
				return makeInstanceGenericInstanceType((GenericInstanceType)a);
			case CecilType.GenericParameter:
				return makeInstanceGenericParameter((GenericParameter)a);
			case CecilType.OptionalModifierType:
				return makeInstanceOptionalModifierType((OptionalModifierType)a);
			case CecilType.PinnedType:
				return makeInstancePinnedType((PinnedType)a);
			case CecilType.PointerType:
				return makeInstancePointerType((PointerType)a);
			case CecilType.RequiredModifierType:
				return makeInstanceRequiredModifierType((RequiredModifierType)a);
			case CecilType.SentinelType:
				return makeInstanceSentinelType((SentinelType)a);
			case CecilType.TypeDefinition:
				return makeInstanceTypeDefinition((TypeDefinition)a);
			case CecilType.TypeReference:
				return makeInstanceTypeReference((TypeReference)a);
			default:
				throw new ApplicationException(string.Format("Unknown cecil type {0}", type));
			}
		}

		ArrayType makeInstanceArrayType(ArrayType a) {
			var rv = new ArrayType(makeInstance(a.ElementType));
			if (!a.IsVector) {
				foreach (var dim in a.Dimensions)
					rv.Dimensions.Add(dim);
			}
			return rv;
		}

		ByReferenceType makeInstanceByReferenceType(ByReferenceType a) {
			return new ByReferenceType(makeInstance(a.ElementType));
		}

		FunctionPointerType makeInstanceFunctionPointerType(FunctionPointerType a) {
			var rv = new FunctionPointerType();
			rv.function = MethodReferenceInstance.make(a.function, git, gim);
			checkModified(a.function, rv.function);
			return rv;
		}

		GenericInstanceType makeInstanceGenericInstanceType(GenericInstanceType a) {
			var rv = new GenericInstanceType(makeInstance(a.ElementType));
			foreach (var arg in a.GenericArguments)
				rv.GenericArguments.Add(makeInstance(arg));
			return rv;
		}

		TypeReference makeInstanceGenericParameter(GenericParameter a) {
			switch (a.Type) {
			case GenericParameterType.Type:
				if (git == null || a.Position >= git.GenericArguments.Count ||
					!MemberReferenceHelper.compareTypes(git.ElementType, a.Owner as TypeReference)) {
					return a;
				}
				modified = true;
				return makeInstance(git.GenericArguments[a.Position]);

			case GenericParameterType.Method:
				if (gim == null || a.Position >= gim.GenericArguments.Count)
					return a;
				modified = true;
				return makeInstance(gim.GenericArguments[a.Position]);

			default:
				return a;
			}
		}

		OptionalModifierType makeInstanceOptionalModifierType(OptionalModifierType a) {
			return new OptionalModifierType(makeInstance(a.ModifierType), makeInstance(a.ElementType));
		}

		PinnedType makeInstancePinnedType(PinnedType a) {
			return new PinnedType(makeInstance(a.ElementType));
		}

		PointerType makeInstancePointerType(PointerType a) {
			return new PointerType(makeInstance(a.ElementType));
		}

		RequiredModifierType makeInstanceRequiredModifierType(RequiredModifierType a) {
			return new RequiredModifierType(makeInstance(a.ModifierType), makeInstance(a.ElementType));
		}

		SentinelType makeInstanceSentinelType(SentinelType a) {
			return new SentinelType(makeInstance(a.ElementType));
		}

		TypeReference makeInstanceTypeDefinition(TypeDefinition a) {
			return makeInstanceTypeReference(a);
		}

		TypeReference makeInstanceTypeReference(TypeReference a) {
			return a;
		}
	}

	public abstract class MultiTypeRefInstance : RefInstance {
		GenericInstanceType git;
		IGenericInstance gim;

		public MultiTypeRefInstance(GenericInstanceType git, IGenericInstance gim = null) {
			this.git = git;
			this.gim = gim;
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

		public static MethodReference make(MethodReference methodReference, GenericInstanceType git, IGenericInstance gim = null) {
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
