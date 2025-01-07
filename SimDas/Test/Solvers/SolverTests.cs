using SimDas.Models.Common;
using SimDas.Models.Solver.Fixed;
using SimDas.Models.Solver.Variable;
using SimDas.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting; // MSTest 프레임워크 사용
using Moq;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace SimDas.Test.Solvers
{
    [TestClass] // MSTest 애트리뷰트 추가
    public class SolverTests
    {
        private Mock<ILoggingService> _mockLogger;
        private DAESystem _testSystem;
        private double[] _initialState;

        [TestInitialize] // 각 테스트 전에 실행
        public void Initialize()
        {
            _mockLogger = new Mock<ILoggingService>();

            // 테스트용 DAE 시스템 설정
            _testSystem = (t, y, yp) =>
            {
                // y[0]: x, y[1]: y, y[2]: z
                double[] res = new double[3];
                res[0] = yp[0] - y[1];              // x' = y
                res[1] = yp[1] - y[2] * y[0];       // y' = z*x
                res[2] = y[0] * y[0] + y[1] * y[1] - 1.0;  // 구속조건: x² + y² = 1
                return res;
            };

            _initialState = new double[] { 1.0, 0.0, 0.0 };
        }

        [TestMethod]
        public async Task ExplicitEuler_ShouldPreserveConstraints()
        {
            // Arrange
            var solver = new ExplicitEulerSolver();
            solver.SetDAESystem(_testSystem, 3);
            solver.InitialState = _initialState;
            solver.StartTime = 0;
            solver.EndTime = 1;
            solver.Intervals = 1000;

            // Act
            var solution = await solver.SolveAsync();

            // Assert
            foreach (var state in solution.States)
            {
                double constraint = state[0] * state[0] + state[1] * state[1];
                Assert.IsTrue(Math.Abs(constraint - 1.0) < 0.1,
                    $"Constraint violation: {Math.Abs(constraint - 1.0)}");
            }
        }

        [TestMethod]
        public async Task ImplicitEuler_ShouldConverge()
        {
            // Arrange
            var solver = new ImplicitEulerSolver();
            solver.SetDAESystem(_testSystem, 3);
            solver.InitialState = _initialState;
            solver.StartTime = 0;
            solver.EndTime = 1;
            solver.Intervals = 100;

            // Act
            var solution = await solver.SolveAsync();

            // Assert
            Assert.AreEqual(solver.Intervals + 1, solution.States.Count);
            Assert.AreEqual(solver.EndTime, solution.TimePoints.Last());
        }

        [TestMethod]
        public async Task DASSL_ShouldHandleStiffSystem()
        {
            // Arrange - 강성이 있는 시스템
            DAESystem stiffSystem = (double t, double[] y, double[] yp) =>
            {
                double[] res = new double[2];
                res[0] = yp[0] + 1000 * y[0];    // 빠른 모드
                res[1] = yp[1] + y[1];           // 느린 모드
                return res;
            };

            var solver = new DasslSolver();
            solver.SetDAESystem(stiffSystem, 2);
            solver.InitialState = new double[] { 1.0, 1.0 };
            solver.StartTime = 0;
            solver.EndTime = 1;

            // Act
            var solution = await solver.SolveAsync();

            // Assert
            Assert.IsTrue(Math.Abs(solution.States.Last()[0]) < 1e-3,
                $"First component should decay quickly: {solution.States.Last()[0]}");
            Assert.IsTrue(Math.Abs(solution.States.Last()[1]) < 0.4,
                $"Second component should decay slowly: {solution.States.Last()[1]}");
        }
    }
}