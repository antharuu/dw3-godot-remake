namespace Dw3Production.Diagnostics.Battle;

/// <summary>Classifies one catalog defect without collapsing it into a generic render crash.</summary>
public enum FighterSheetFailure
{
    /// <summary>The model document cannot be parsed or lacks its fighter identifier.</summary>
    DocumentInvalid,

    /// <summary>The document has no drawable mesh geometry.</summary>
    GeometryMissing,

    /// <summary>The document has no usable texture page or a referenced page image is absent.</summary>
    TextureMissing,

    /// <summary>The document has no source motion sequence.</summary>
    MotionMissing,

    /// <summary>The document has no camera family suitable for its camp.</summary>
    CameraMissing,

    /// <summary>The production renderer emitted no silhouette against the paired stage frame.</summary>
    SilhouetteEmpty,

    /// <summary>The production renderer rejected an otherwise catalogued model.</summary>
    RendererError,

    /// <summary>The source camera needed diagnostic pull-back to keep the fighter inside the raster.</summary>
    DiagnosticPullBack,
}
