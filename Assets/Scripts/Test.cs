using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class Test : MonoBehaviour
{
    public Texture2D testTex;
    public ComputeShader shader;

    public Button btnReset;
    public Button btnStat;
    public ScratchImage scratchImage;

    ComputeBuffer buffer;
    uint[] arrData;

    void Start()
    {
        btnReset.onClick.AddListener(() => scratchImage.ResetMask());
        btnStat.onClick.AddListener(RunCompute);

        buffer = new ComputeBuffer(5, 4);
        arrData = new uint[5];
        buffer.SetData(arrData);
    }

    private void OnDestroy()
    {
        buffer.Release();
    }

    private void Update()
    {
        RunCompute();
    }

    private void RunCompute()
    {
        int clearKernl = shader.FindKernel("HistogramClear");
        shader.SetBuffer(clearKernl, "_HistogramBuffer", buffer);
        shader.Dispatch(clearKernl, testTex.width / 2 + 1, 1, 1);

        int setKnerl = shader.FindKernel("Histogram");
        shader.SetTexture(setKnerl, "_Source", testTex);
        shader.SetBuffer(setKnerl, "_HistogramBuffer", buffer);
        shader.SetVector("_TexSize", new Vector2(testTex.width, testTex.height));
        shader.Dispatch(setKnerl, testTex.width / 2 + 1, testTex.height / 2 + 1, 1);

        buffer.GetData(arrData);

        //Debug.Log($"data:{arrData[0]},{arrData[1]},{arrData[2]},{arrData[3]},{arrData[4]}");

    }
}
