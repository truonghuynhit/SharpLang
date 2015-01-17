// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace System.Reflection.Metadata
{
    struct ManifestResource
    {
        private readonly MetadataReader reader;

        // Workaround: JIT doesn't generate good code for nested structures, so use RowId.
        private readonly uint rowId;

        internal ManifestResource(MetadataReader reader, ManifestResourceHandle handle)
        {
            DebugCorlib.Assert(reader != null);
            DebugCorlib.Assert(!handle.IsNil);

            this.reader = reader;
            this.rowId = handle.RowId;
        }

        private ManifestResourceHandle Handle
        {
            get { return ManifestResourceHandle.FromRowId(rowId); }
        }

        /// <summary>
        /// Specifies the byte offset within the referenced file at which this resource record begins.
        /// </summary>
        /// <remarks>
        /// Corresponds to Offset field of ManifestResource table in ECMA-335 Standard.
        /// </remarks>
        public long Offset
        {
            get { return reader.ManifestResourceTable.GetOffset(Handle); }
        }

        /// <summary>
        /// Resource attributes.
        /// </summary>
        /// <remarks>
        /// Corresponds to Flags field of ManifestResource table in ECMA-335 Standard.
        /// </remarks>
        public ManifestResourceAttributes Attributes
        {
            get { return reader.ManifestResourceTable.GetFlags(Handle); }
        }

        /// <summary>
        /// Name of the resource.
        /// </summary>
        /// <remarks>
        /// Corresponds to Name field of ManifestResource table in ECMA-335 Standard.
        /// </remarks>
        public StringHandle Name
        {
            get { return reader.ManifestResourceTable.GetName(Handle); }
        }

        /// <summary>
        /// <see cref="AssemblyFileHandle"/>, <see cref="AssemblyReferenceHandle"/>, or nil handle.
        /// </summary>
        /// <remarks>
        /// Corresponds to Implementation field of ManifestResource table in ECMA-335 Standard.
        /// 
        /// If nil then <see cref="Offset"/> is an offset in the PE image that contains the metadata, 
        /// starting from the Resource entry in the CLI header.
        /// </remarks>
        public Handle Implementation
        {
            get { return reader.ManifestResourceTable.GetImplementation(Handle); }
        }

        public CustomAttributeHandleCollection GetCustomAttributes()
        {
            return new CustomAttributeHandleCollection(reader, Handle);
        }
    }
}