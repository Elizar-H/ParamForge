#pragma once

#include <QJsonArray>
#include <QJsonObject>
#include <QStringList>
#include <QVector>
#include <initializer_list>
#include <optional>

namespace pf
{
inline constexpr auto Version = PARAMFORGE_VERSION;

const QJsonObject& schema();
QString number(double value, int decimals = 4);
QString localized(double value, int decimals = 4);
double roundTo(double value, int decimals = 3);
double target(double nominal, double lower, double upper);
qint64 dimensionTicks(double value);
double roundedDimensionSum(std::initializer_list<double> terms);

struct Field
{
    QString key, label, unit, group, note;
    double minimum = 0, maximum = 0, step = 0;
};
struct Dimension
{
    QString key, nominal, lower, upper, designation;
    QStringList positions;
};
const QVector<Field>& fields();
const QVector<Dimension>& dimensions();
const Field& field(const QString& key);
const Dimension& dimension(const QString& key);

class Parameters
{
  public:
    Parameters();
    explicit Parameters(const QJsonObject& overrides);
    double n(const QString& key) const;
    QString s(const QString& key) const;
    bool b(const QString& key) const;
    void set(const QString& key, const QJsonValue& value);
    const QJsonObject& json() const
    {
        return values;
    }
    bool operator==(const Parameters& other) const = default;

  private:
    QJsonObject values;
};

struct Error
{
    QString field, message;
};
struct Calculations
{
    double stockLength = 0, cutLength = 0, clearOut = 0, clearIn = 0;
    double totalCutMinutes = 0, targetBoreDiameter = 0, targetCounterboreDiameter = 0;
    double targetFlangeDiameter = 0, targetLength = 0, targetCounterboreDepth = 0;
    double cutoffWidth = 3, partingFrontZ = 0, partingBackZ = 0, jawsFrontZ = 0;
    double rawStockLength = 0, stockOverhang = 0, gripLength = 0, chuckClearance = 0;
    double secondClampStart = 0, secondClampEnd = 0;
    double softJawPocketDiameter = 0, softJawPocketDepth = 0;
    double targetBodyDiameter = 0, targetFlangeWidth = 0;
    QJsonObject json() const;
};
struct Move
{
    double x = 0, z = 0;
    QString mode, transition;
    int line = 0;
    std::optional<double> feed;
    double rpm = 0;
};
struct ParsedMove
{
    double x = 0, z = 0;
    QString mode;
    int line = 0, physicalLine = 0, tool = 0;
    std::optional<double> feed, rpm;
};
struct Transition
{
    QString id, title, tool, kind;
    double diameter = 0, startDiameter = 0, rpm = 0, feed = 0, depth = 0;
    double referenceDiameter = 0, zStart = 0, zEnd = 0, cutMinutes = 0;
    QVector<double> passes;
    QVector<int> blocks;
    double actualMaximumDepth() const;
};
struct Operation
{
    QString code, title, kind, description;
};
struct Setup
{
    QString id, title;
    bool flipped = false;
    double stockFrontZ = 0, jawsFrontZ = 0, gripLength = 0;
    double gripDiameter = 0, rearZ = 0;
};
enum class InstructionKind
{
    Prepare,
    Diameter,
    CoolantOff,
    Stop,
    Tool,
    GeometryOffset,
    CoolantOn,
    Speed,
    FeedPerRevolution,
    FeedPerMinute,
    Comment,
    Motion,
    End
};
struct Instruction
{
    InstructionKind kind;
    Move motion;
    int tool = 0;
    double value = 0;
    QString text;
};
struct Plan
{
    QString id;
    Setup setup;
    struct Blank
    {
        double frontZ, rearZ, jawsZ, outerDiameter, innerDiameter;
    };
    std::optional<Blank> blank;
    QVector<Transition> transitions;
    QVector<Instruction> instructions;
};
struct Verification
{
    bool ok = false;
    QStringList errors;
    QVector<ParsedMove> moves;
    double cutMinutes = 0;
};
struct Program
{
    QString setupId, name, fileName, text;
    Setup setup;
    QVector<Move> moves;
    QVector<Transition> transitions;
    Verification verification;
};
struct Project
{
    QString id;
    Parameters parameters;
    Calculations calculations;
    QVector<Operation> operations;
    QVector<Program> programs;
};
struct Generation
{
    QVector<Error> errors;
    std::optional<Project> project;
    bool ok() const
    {
        return project.has_value() && errors.isEmpty();
    }
};

double toleranceWidth(double nominal, int grade);
std::pair<double, double> deviations(const Dimension&, double nominal, const QString& designation);
Parameters resolveTolerances(Parameters parameters);
Parameters setTolerance(Parameters parameters, const QString& key, const QString& designation);
Parameters resolveStock(Parameters parameters);
std::pair<double, double> boreLimits(const Parameters&);
double minimumStockDiameter(const Parameters&);
Calculations calculate(const Parameters&);
struct BoringTool
{
    QString id, name;
    double diameter, minBore, offset, length, minOverhang, maxOverhang;
};
const QVector<BoringTool>& boringTools();
QString partingToolName(const Parameters&);
BoringTool boringTool(const Parameters&);
QVector<Error> validateToolSetup(const Parameters&);
QVector<Error> validate(const Parameters&);
QVector<double> passes(double start, double finish, double depth);
Plan firstSetup(const Parameters&, const Calculations&, const QString& id);
Plan secondSetup(const Parameters&, const Calculations&, const QString& id);
Program postprocess(const Plan&, const Calculations&, const QString& controller);
Verification verify(const QString& text, const Parameters&, const Setup&);
Generation generate(const Parameters&);
QVector<Operation> operations(const Parameters&, const Calculations&);
QString inspectionDescription(const Parameters&, const Calculations&);
QJsonObject projectJson(const Project&);
} // namespace pf
