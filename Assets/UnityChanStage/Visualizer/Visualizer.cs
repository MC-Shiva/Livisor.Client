using UnityEngine;

[RequireComponent(typeof(Renderer))]
public sealed class Visualizer : MonoBehaviour
{
    private static readonly int SpectraId = Shader.PropertyToID("_Spectra");
    private static readonly int CenterId = Shader.PropertyToID("_Center");

    public Reaktion.ReaktorLink spectrum1;
    public Reaktion.ReaktorLink spectrum2;
    public Reaktion.ReaktorLink spectrum3;
    public Reaktion.ReaktorLink spectrum4;
    public Vector4 spectrum;

    private Renderer targetRenderer;
    private MaterialPropertyBlock propertyBlock;

    private void Awake()
    {
        targetRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();

        spectrum1.Initialize(this);
        spectrum2.Initialize(this);
        spectrum3.Initialize(this);
        spectrum4.Initialize(this);
    }

    private void LateUpdate()
    {
        spectrum = new Vector4(
            Mathf.Clamp01(spectrum1.Output),
            Mathf.Clamp01(spectrum2.Output),
            Mathf.Clamp01(spectrum3.Output),
            Mathf.Clamp01(spectrum4.Output)
        );

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetVector(SpectraId, spectrum);
        propertyBlock.SetVector(CenterId, transform.position);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}
