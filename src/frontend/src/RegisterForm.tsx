import { useState, FormEvent } from "react";

interface RegisterFormProps {
    onRegisterComplete: (userData: UserData) => void;
}

export interface UserData {
    name: string;
    email: string;
    phone: string;
    position?: string;
    company?: string;
}

const BACKEND_HTTP_BASE = (import.meta.env.VITE_BACKEND_BASE as string | undefined) ?? "";

function RegisterForm({ onRegisterComplete }: RegisterFormProps) {
    const [name, setName] = useState("");
    const [email, setEmail] = useState("");
    const [phone, setPhone] = useState("");
    const [position, setPosition] = useState("");
    const [company, setCompany] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const handleSubmit = async (e: FormEvent) => {
        e.preventDefault();
        setError(null);

        if (!name.trim() || !email.trim() || !phone.trim()) {
            setError("Nome, email e telefone são obrigatórios");
            return;
        }

        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!emailRegex.test(email)) {
            setError("Por favor, insira um email válido");
            return;
        }

        const phoneRegex = /^[\d\s\-\(\)\+]+$/;
        if (!phoneRegex.test(phone)) {
            setError("Por favor, insira um telefone válido");
            return;
        }

        setLoading(true);

        try {
            const userData: UserData = {
                name: name.trim(),
                email: email.trim(),
                phone: phone.trim(),
            };

            if (position.trim()) {
                userData.position = position.trim();
            }
            if (company.trim()) {
                userData.company = company.trim();
            }

            const response = await fetch(`${BACKEND_HTTP_BASE}/createuser`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                },
                body: JSON.stringify(userData),
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.detail || `Erro ao cadastrar: ${response.status}`);
            }

            onRegisterComplete(userData);
        } catch (err) {
            console.error('Registration error:', err);
            setError(err instanceof Error ? err.message : "Erro ao realizar cadastro");
            setLoading(false);
        }
    };

    return (
        <div className="register-container">
            <div className="register-card">
                {/* Logo Section */}
                <div className="register-logo">
                    <div className="logo-placeholder">
                        {/* Substitua este div pela sua logo */}
                        <img src="img/logo-blue.png" />
                        <h1>Azure Voice Live Avatar</h1>
                    </div>
                    
                </div>

                <div className="register-header">
                    <p>Cadastre-se para iniciar sua experiência com nosso assistente virtual</p>
                </div>

                <form onSubmit={handleSubmit} className="register-form">
                    {/* Campos Obrigatórios */}
                    <div className="form-section">
                        <h3 className="section-title">Informações Principais</h3>
                        
                        <div className="form-row">
                            <div className="form-group">
                                <label htmlFor="name">
                                    Nome Completo <span className="required">*</span>
                                </label>
                                <input
                                    id="name"
                                    type="text"
                                    value={name}
                                    onChange={(e) => setName(e.target.value)}
                                    placeholder="Digite seu nome completo"
                                    disabled={loading}
                                    required
                                />
                            </div>

                            <div className="form-group">
                                <label htmlFor="email">
                                    E-mail <span className="required">*</span>
                                </label>
                                <input
                                    id="email"
                                    type="email"
                                    value={email}
                                    onChange={(e) => setEmail(e.target.value)}
                                    placeholder="seu.email@exemplo.com"
                                    disabled={loading}
                                    required
                                />
                            </div>
                        </div>

                        <div className="form-row">
                            <div className="form-group">
                                <label htmlFor="phone">
                                    Telefone <span className="required">*</span>
                                </label>
                                <input
                                    id="phone"
                                    type="tel"
                                    value={phone}
                                    onChange={(e) => setPhone(e.target.value)}
                                    placeholder="(11) 99999-9999"
                                    disabled={loading}
                                    required
                                />
                            </div>

                            {/* Espaço vazio para manter o layout */}
                            <div className="form-group-spacer"></div>
                        </div>
                    </div>

                    {/* Campos Opcionais */}
                    <div className="form-section">
                        <h3 className="section-title optional">
                            Informações Adicionais 
                        </h3>
                        
                        <div className="form-row">
                            <div className="form-group">
                                <label htmlFor="position">Cargo</label>
                                <input
                                    id="position"
                                    type="text"
                                    value={position}
                                    onChange={(e) => setPosition(e.target.value)}
                                    placeholder="Ex: Desenvolvedor, Gerente, Analista..."
                                    disabled={loading}
                                />
                            </div>

                            <div className="form-group">
                                <label htmlFor="company">Empresa</label>
                                <input
                                    id="company"
                                    type="text"
                                    value={company}
                                    onChange={(e) => setCompany(e.target.value)}
                                    placeholder="Nome da empresa onde trabalha"
                                    disabled={loading}
                                />
                            </div>
                        </div>
                    </div>

                    {error && (
                        <div className="error-message">
                            <span className="error-icon">⚠️</span>
                            <span>{error}</span>
                        </div>
                    )}

                    <button
                        type="submit"
                        className="register-submit-button"
                        disabled={loading}
                    >
                        {loading ? (
                            <>
                                <span className="button-spinner"></span>
                                Cadastrando...
                            </>
                        ) : (
                            <>
                                <span>Iniciar Conversa com o Avatar</span>
                                <span className="button-arrow">→</span>
                            </>
                        )}
                    </button>
                </form>

                <div className="register-footer">
                    <p>
                        <span className="required">*</span> Campos obrigatórios
                        <span className="separator">•</span>
                        Seus dados poderão ser utilizados para futuros contatos
                    </p>
                </div>
            </div>
        </div>
    );
}

export default RegisterForm;