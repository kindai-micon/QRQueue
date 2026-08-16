export default function Index({ model }: { model: { name: string } }) {
    return (
        <div>
            <h1>Hello {model.name}!</h1>
            <p>JsxCore (Preact) によるサーバーレンダリングのデモです。</p>
        </div>
    );
}
