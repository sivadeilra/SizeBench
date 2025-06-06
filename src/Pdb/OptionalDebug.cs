namespace Pdb;

/// <summary>
/// Identifies optional 
/// </summary>
public enum OptionalDebugStream : int {
    /// Stream contains an array of `FPO_DATA` structures. This contains the relocated contents of
    /// any `.debug$F` section from any of the linker inputs.
    fpo_data = 0,
    /// Stream contains a debug data directory of type `IMAGE_DEBUG_TYPE_EXCEPTION`.
    exception_data = 1,
    /// Stream contains a debug data directory of type `IMAGE_DEBUG_TYPE_FIXUP`.
    fixup_data = 2,
    /// Stream contains a debug data directory of type `IMAGE_DEBUG_TYPE_OMAP_TO_SRC`.
    /// This is used for mapping addresses from instrumented code to uninstrumented code.
    omap_to_src_data = 3,
    /// Stream contains a debug data directory of type `IMAGE_DEBUG_TYPE_OMAP_FROM_SRC`.
    /// This is used for mapping addresses from uninstrumented code to instrumented code.
    omap_from_src_data = 4,
    /// A dump of all section headers from the original executable.
    section_header_data = 5,
    token_to_record_id_map = 6,
    /// Exception handler data
    xdata = 7,
    /// Procedure data
    pdata = 8,
    new_fpo_data = 9,
    original_section_header_data = 10,
}
