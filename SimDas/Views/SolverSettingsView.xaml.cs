using SimDas.Models.Common;
using SimDas.Views.Converters;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SimDas.Views
{
    /// <summary>
    /// Interaction logic for SolverSettingsView.xaml
    /// </summary>
    public partial class SolverSettingsView : UserControl
    {
        public SolverSettingsView()
        {
            InitializeComponent();
        }

        private void SelectedSolverType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedSolverType.SelectedValue is SolverType selectedSolverType)
            {
                UpdateSolverDescription(selectedSolverType);
            }
        }

        private void UpdateSolverDescription(SolverType selectedSolverType)
        {
            // 새 FlowDocument 생성
            var flowDocument = new FlowDocument();

            // Solver 설명 가져오기
            var solverDescriptionConverter = new SolverDescriptionConverter();
            solverDescriptionConverter.SolverDescriptions.TryGetValue(selectedSolverType, out string description);

            // 설명 추가
            var descriptionParagraph = new Paragraph();
            if (!string.IsNullOrEmpty(description))
            {
                // Solver 이름을 Bold로 처리
                var solverNames = new List<string> { "ExplicitEuler", "RungeKutta4", "ImplicitEuler", "DASSL" };
                var words = description.Split(' ');

                foreach (var word in words)
                {
                    Run run;

                    // Solver 이름이면 Bold 스타일 적용
                    if (solverNames.Exists(name => word.Contains(name)))
                    {
                        var bold = new Bold(new Run(word));
                        descriptionParagraph.Inlines.Add(bold);
                    }
                    else
                    {
                        // 일반 텍스트
                        run = new Run(word);
                        descriptionParagraph.Inlines.Add(run);
                    }

                    // 단어 간 공백 추가
                    descriptionParagraph.Inlines.Add(new Run(" "));
                }
            }
            else
            {
                descriptionParagraph.Inlines.Add(new Run("No description available."));
            }

            // FlowDocument에 추가
            flowDocument.Blocks.Add(descriptionParagraph);

            // RichTextBox에 설정
            SolverDescription.Document = flowDocument;
        }
    }
}
